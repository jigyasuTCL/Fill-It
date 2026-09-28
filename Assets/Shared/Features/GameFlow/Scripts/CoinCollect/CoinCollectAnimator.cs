using System.Collections.Generic;
using DG.Tweening;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.UI;

// The replacement for the now-obsolete UIHelper.AnimateCoin. Same job — move a handful of coins from
// wherever the reward is shown into the coin HUD — but staged in four beats, built against a reference
// video the developer supplied.
//
//   0. BEAT    a quarter second where nothing moves at all. It is what turns the collect into a
//              payoff rather than just more motion continuing from the popup's entry.
//   1. BLOOM   coins appear ONE AT A TIME at the reward icon, growing from nothing, and spread out
//              along gently bowed paths into a loose cloud centred on it.
//   2. HOVER   the cloud holds, every coin bobbing on its own phase and a slightly different period,
//              so it shimmers instead of pulsing in unison.
//   3. STREAM  every coin flies THE SAME curved route into the counter, offset only in time, so they
//              read as one continuous ribbon — beads threaded on a string. The launch rate accelerates
//              through the stream: a trickle that builds into a dense pour.
//
// EVERY NUMBER LIVES IN CoinCollectSettings, a ScriptableObject asset wired straight onto the screen
// that plays this, so the feel is a designer's to tune. Read that file for what each value does and
// why; this one is only the mechanism.
// What follows is the handful of decisions that are NOT tunable, because each is structural — expose
// any of them and the effect stops being the effect.
//
// THE COINS NEVER ROTATE. Not a spin, not a flip, not a tumble, and no squash or stretch — they stay
// flat, upright and facing the camera from the moment they appear to the moment they land. This is the
// single most defining property of the look the reference establishes, and a rotating coin reads as a
// completely different effect. An early pass had a burst-spin tunable; it was removed rather than set
// to zero, precisely so it cannot be switched back on by someone who has not seen the reference.
//
// THE STREAM'S ROUTE IS SHARED, NOT PER COIN, and its flight is LINEAR. Every coin bends around the
// same control point into the same target, which is the whole reason the stream reads as a ribbon;
// giving each coin its own bow — as an early pass did, alternating the side by index — scatters it and
// loses the effect entirely. And evenly spaced beads on a string require constant speed, so an eased
// flight bunches the chain at one end. Only the arc's WIDTH is exposed, never its shape.
//
// THE BLOOM EASES IN, the stream does not. That asymmetry is deliberate and is what makes coins look
// like they come from the centre — see Burst for the full explanation, because the reason is genuinely
// counter-intuitive and it cost several passes to find.
//
// THE COINS ARE MEANT TO OVERLAP. The cloud is dense by design, which puts the whole weight of looking
// good on the draw order being stable — see AssignDrawOrder for what that costs and why.
//
// Why a helper instead of tuning UIHelper.AnimateCoin: the old helper's counter roll was a single 0.5s
// tween started by the first arrival, so on a large reward the number finished a full second before the
// last coin landed. Here the caller is told about every arrival (onCoinArrived) and nothing else drives
// the number, so the count and the coins cannot desync however many coins there are or however slow the
// stream is tuned. The old method is left in place, unchanged, for any screen still on it.
//
// Static and stateless, like the rest of UIHelper: nothing is held between calls. Each coin lives only
// inside its own tween chain and destroys itself on arrival, and the DOTween.KillAll that every scene
// change already performs tears the whole thing down with nothing left to clean up.
public static class CoinCollectAnimator
{
    // Angles walk the cloud by the golden angle. An even fan reads as a mechanical clock face and a
    // random angle visibly clumps at these counts; this spaces them without either problem, and being
    // fixed it produces the same quality of spread on every run. It is also what keeps the cloud's
    // draw order looking mixed — see AssignDrawOrder. Not exposed: it is the one value here with a
    // single correct answer.
    private const float GoldenAngleDegrees = 137.508f;

    // THE SETTINGS COME FROM THE CALLER, wired as a direct object reference on the screen that plays
    // the animation. That is the whole path in a build: no Resources.Load, no lookup by name, no
    // runtime search — the asset is a hard dependency of the prefab, so Unity pulls it into the build
    // automatically and it is already in memory by the time this runs.
    //
    // This only falls back when the field was left unwired, and then it goes through
    // CoinCollectSettings.Instance, which loads from Resources and, failing that, hands back a
    // throwaway instance carrying the defaults. Both steps log. The fallback exists because this
    // animation is the last thing standing between a level win and the next scene loading, and a
    // forgotten inspector reference must not be able to stop the game — it is a safety net, not the
    // intended route. If you see its warning, wire the field.
    private static CoinCollectSettings Resolve(CoinCollectSettings settings)
    {
        if (settings) return settings;

        Debug.LogWarning($"[CoinCollectAnimator] No {nameof(CoinCollectSettings)} was passed in — the "
                         + "reference on the screen playing this animation is not wired. Falling back "
                         + "to a Resources lookup.");

        return CoinCollectSettings.Instance;
    }

    /// <summary>
    /// How many coin sprites a reward of <paramref name="rewardCoins"/> is shown as. Lives here rather
    /// than on the caller because it is part of the same tuning set as the animation itself.
    /// </summary>
    /// <remarks>
    /// Never rounds down to zero while there is still a reward to show: a count of zero means nothing
    /// ever arrives, and callers gate their scene flow on the arrival callback.
    /// </remarks>
    public static int CoinCountFor(int rewardCoins, CoinCollectSettings settings)
    {
        if (rewardCoins <= 0) return 0;

        settings = Resolve(settings);
        var perCoin = Mathf.Max(1, settings.currencyPerCoin);

        return Mathf.Min(Mathf.Max(1, settings.maxCoins), Mathf.Max(1, rewardCoins / perCoin));
    }

    /// <summary>
    /// How long it is from <see cref="Play"/> being called to the last coin settling into the cloud —
    /// the opening beat, plus the bloom's launch window, plus one coin's trip out. Exposed so a caller
    /// can run its own animation across roughly that window without having to duplicate the arithmetic,
    /// or drift out of step with it when the bloom is retuned.
    /// </summary>
    /// <remarks>
    /// <paramref name="coinCount"/> is accepted but not read: the bloom is built from a fixed span
    /// rather than a per-coin gap, so how many coins there are changes only how densely that window is
    /// filled. The parameter is kept so the signature stays stable for callers. Accurate to within one
    /// jittered gap, so treat it as a window rather than a frame-exact deadline.
    /// </remarks>
    public static float BurstWindowFor(int coinCount, CoinCollectSettings settings)
    {
        settings = Resolve(settings);

        return settings.startDelay + settings.BurstSpan + settings.BurstDuration;
    }

    /// <summary>
    /// Bloom <paramref name="coinCount"/> coins out of <paramref name="burstOrigin"/> into a loose
    /// cloud around it, hover, then stream them along one shared curve into
    /// <paramref name="targetPosition"/> (the coin HUD).
    /// </summary>
    /// <param name="onCoinSpawned">(spawnedCount, totalCount) as each coin APPEARS at the origin,
    /// during the bloom. Drive anything the coins are visibly leaving — the reward number they are
    /// pouring out of — from this rather than from arrivals, so it empties as they go rather than a
    /// beat later.</param>
    /// <param name="onCoinArrived">(arrivedCount, totalCount) per landing at the counter — drive the
    /// wallet from this, not from a roll of your own, or the number will finish before the coins do.
    /// </param>
    /// <param name="onAllCoinsComplete">Fires exactly once, on the last landing. Guaranteed to fire
    /// even when there is nothing to animate, because callers gate scene flow on it.</param>
    public static void Play(
        int coinCount,
        Image coinPrefab,
        Transform parent,
        Vector3 burstOrigin,
        Vector3 targetPosition,
        Vector3 targetSize,
        CoinCollectSettings coinCollectSettings,
        UnityAction<int, int> onCoinSpawned,
        UnityAction<int, int> onCoinArrived,
        UnityAction onAllCoinsComplete)
    {
        // The old helper's completion contract was "fire when arrivedCount == coinCount", which never
        // fires at all when coinCount is 0 — and the level-complete popup gates the next scene on that
        // callback, so a reward small enough to round down to zero coins silently soft-locked the game.
        // Answer straight away instead of leaving the caller waiting on an event that cannot arrive.
        if (coinCount <= 0 || coinPrefab == null || parent == null)
        {
            onAllCoinsComplete?.Invoke();
            return;
        }

        var settings = Resolve(coinCollectSettings);
        var count = Mathf.Min(coinCount, Mathf.Max(1, settings.maxCoins));

        // Captured once, in the parent's local space, because every coin tweens locally: the container
        // is a plain RectTransform under the same canvas, so one conversion serves all of them.
        //
        // NOTE the origin is the raw burst point with NO vertical offset applied. Where the CLOUD sits
        // and where the coins COME FROM are separate questions, so burstOffsetY lives in BurstOffset,
        // where it moves the resting positions only. Applying it here too would move the launch point
        // as well, and the coins would appear in mid-air above the reward.
        var originLocal = parent.InverseTransformPoint(burstOrigin);
        var targetLocal = parent.InverseTransformPoint(targetPosition);

        // Scale, never sizeDelta, all the way through. Tweening sizeDelta dirties the coin's own canvas
        // every frame, and each coin carries its own canvas, so a flight of overlapping coins was
        // forcing a rebuild per coin per frame. Scale never touches layout.
        var authoredWidth = Mathf.Max(1f, coinPrefab.rectTransform.sizeDelta.x);
        var arrivalScale = Mathf.Max(0.01f, targetSize.x / authoredWidth);

        var coins = new List<Image>(count);

        // Same schedule shape as the stream's, from the same builder — see BuildLaunchSchedule.
        var burstLaunch = BuildLaunchSchedule(count, settings.BurstSpan, settings.burstRateRamp,
            settings.burstStaggerJitter);

        // One delegate shared by every coin rather than one each: they all close over the same counter,
        // and the bloom's schedule preserves appearance order, so the count it reports is in step with
        // what is on screen.
        var spawned = 0;

        UnityAction onSpawned = delegate
        {
            spawned++;
            onCoinSpawned?.Invoke(spawned, count);
        };

        for (var i = 0; i < count; i++)
        {
            var coin = Object.Instantiate(coinPrefab, parent);
            var rect = coin.rectTransform;

            rect.localPosition = originLocal;
            rect.localScale = Vector3.one * settings.burstSpawnScale;

            // Explicit, not assumed. Nothing in here ever writes rotation again, so this is the single
            // place that guarantees a coin is upright — including when the art prefab is authored at an
            // angle. See the header: upright throughout is the defining property of the look.
            rect.localRotation = Quaternion.identity;

            // Stopped explicitly rather than assumed off, in case playOnAwake is set on the prefab.
            SetCoinParticles(coin, settings.playParticlesOnBurst);

            coins.Add(coin);

            var launchAt = settings.startDelay + burstLaunch[i];

            Burst(rect, originLocal, originLocal + BurstOffset(i, count, settings), launchAt, settings);

            // Reported on the SAME clock the bloom is scheduled on, from out here rather than from a
            // callback inside the coin's own sequence. A sequence callback at position 0 was the first
            // attempt and it fired for all the coins at once, in the first frame, because DOTween's
            // SetDelay does not mean the same thing on a Sequence as on a Tweener — depending on
            // version it becomes a PREPENDED INTERVAL, which makes "position 0" the start of the delay
            // rather than the end. Any number a caller drives from this then jumped straight to its
            // final value instead of stepping down once per coin.
            DOVirtual.DelayedCall(launchAt, delegate { onSpawned.Invoke(); });
        }

        // One sound for the whole bloom, not one per coin — a per-coin burst is a wall of identical
        // one-shots, and the ticking belongs to the stream where each tick has a coin to explain it.
        DOVirtual.DelayedCall(settings.startDelay,
            delegate { AudioManager.Instance.PlaySoundAction.Invoke(SoundName.CoinCollect); });

        // Deliberately NOT re-sorted before streaming. An earlier pass ordered the departures by
        // distance to the HUD so the cloud emptied from the counter's side, which made sense when each
        // coin flew its own path from where it sat. It does not survive the shared route: every coin
        // funnels onto the same bend within the first fraction of its flight, so where it started is
        // invisible by the time anyone could read an order into it — and appearance order is a
        // golden-angle walk, which is exactly the "mixed, not layered" stacking the resting cloud wants.
        AssignDrawOrder(coins, coinPrefab);

        ScheduleStream(coins, originLocal, targetLocal, arrivalScale,
            settings.startDelay + burstLaunch[count - 1], settings, onCoinArrived, onAllCoinsComplete);
    }

    // THE FIX FOR THE FLICKER. Every coin instance carries its own Canvas with overrideSorting on and,
    // straight from the prefab, the SAME sortingOrder as all the others. Sorting order is the actual
    // sort key for canvases, so identical values leave the relative order of overlapping coins
    // undefined — and it can change between frames, which is the shimmer over a dense cloud.
    //
    // So every coin gets its own value, in APPEARANCE order, so a later coin always draws over an
    // earlier one. That single assignment covers all three moving beats at once: the resting cloud looks
    // mixed rather than layered (appearance order is a golden-angle walk, so neighbours in the stack are
    // nowhere near each other on screen), and the stream launches in the same order, so later coins draw
    // on top of earlier ones exactly as the reference has them, with nothing to restack mid-flight.
    //
    // The band is derived from whatever the PREFAB's canvas is set to rather than hardcoded, so if
    // these ever need to move relative to other UI the artist retunes the prefab and the whole band
    // follows. Sibling order is set to match, which costs nothing and keeps the stack deterministic
    // even if overrideSorting is ever switched off on the prefab.
    //
    // Audited when this was written: CoinVFX is the only prefab in the GamePlay scene with
    // overrideSorting on, and the scene's own canvas is order 0 with it off — so this band is free.
    private static void AssignDrawOrder(List<Image> coins, Image coinPrefab)
    {
        var prefabCanvas = coinPrefab.GetComponent<Canvas>();
        var baseOrder = prefabCanvas == null ? 0 : prefabCanvas.sortingOrder;

        for (var i = 0; i < coins.Count; i++)
        {
            // Last to appear ends up the last sibling, matching the sorting order below rather than
            // fighting it.
            coins[i].rectTransform.SetAsLastSibling();

            var canvas = coins[i].GetComponent<Canvas>();
            if (canvas == null) continue;

            canvas.overrideSorting = true;
            canvas.sortingOrder = baseOrder + i;
        }
    }

    // ---------------------------------------------------------------------------------------------
    // Beat 1 — the bloom
    // ---------------------------------------------------------------------------------------------

    // The coin grows from nothing at the origin and travels out to its place in the cloud along one
    // gently bowed path, decelerating into it. One path tween, not the two-axis parabola an earlier
    // pass built: a coin whose resting spot is below the origin was being sent up over an apex and then
    // dropped a long way back down, which is a large, busy movement to reach a spot barely away from
    // where it started, and it is a different movement from the one a coin resting above the origin
    // makes. A single proportional bow gives every coin the same shape of trip, scaled to its own
    // distance, which is what makes the set read as one spreading motion.
    //
    // Everything lives in ONE sequence with SetTarget(rect) so that Stream's rect.DOKill() takes the
    // whole thing down as a unit. That matters: the sequence's OnComplete is what starts the idle bob,
    // so a stray surviving sequence would put a fresh infinite bob on a coin that has already left.
    private static void Burst(RectTransform rect, Vector3 originLocal, Vector3 restLocal, float delay,
        CoinCollectSettings settings)
    {
        var travel = restLocal - originLocal;
        var duration = Mathf.Max(0.01f, settings.BurstDuration);

        // Proportional to this coin's own travel, so an inner coin gets a small bow and an outer one a
        // larger one, and neither leaves the cloud's footprint. The variance is the one place
        // randomness is wanted here — identical bows over a golden-angle spread read as a mechanical
        // fountain — and it is applied to a value that is already small, so it cannot produce the
        // wildly differing speeds a fixed apex did.
        var bow = Mathf.Clamp(travel.magnitude * settings.burstArcFactor,
                      settings.burstArcMin, settings.burstArcMax)
                  * Random.Range(1f - settings.burstArcVariance, 1f + settings.burstArcVariance);

        var control = originLocal + travel * 0.5f + Vector3.up * bow;

        var seq = DOTween.Sequence().SetTarget(rect).SetDelay(delay);

        // InOutSine: slow away from the centre, gliding through the middle, slow into place. Two
        // reasons, and the first is not obvious.
        //
        // THIS IS WHAT MAKES THE COIN LOOK LIKE IT CAME FROM THE CENTRE. It previously used OutCubic —
        // an ease-OUT, so fastest at the very start — and paired with a spawn scale of zero that meant
        // the coin was already something like 40% of the way to its resting spot by the time it had
        // grown big enough to see. It genuinely appeared on the origin, every time, and still read as
        // materialising in mid-flight, because the only frames in which it was visible were ones where
        // it had already left. Easing in fixes that at the source: the coin barely moves while it is
        // growing, so the frames in which it becomes visible are frames in which it is still at the
        // centre. (Slowing the whole bloom down did not fix this and could not have — a fixed
        // *fraction* of the trip is spent invisible regardless of how long the trip takes.)
        //
        // Second, a symmetric ease is simply the smoothest shape available for a there-and-stop move.
        // No positional overshoot: the reference's "lands slightly past and settles back" lives entirely
        // in the scale tween below, because an overshoot on a curved path extrapolates along the path's
        // exit direction — unpredictable, and the thing that was reading as a bounce.
        seq.Insert(0f, rect.DOLocalPath(new[] { control, restLocal }, duration, PathType.CatmullRom)
            .SetEase(Ease.InOutSine)
            .SetOptions(false));

        // Zero to full size over the first two thirds of the trip — the other half of the centre-spawn
        // fix. OutBack rises fast and early, so the coin is readable while the InOutSine above still
        // has it near the origin. Finishing early is safe here in a way it was not under OutCubic: the
        // coin is moving slowly at both ends of an InOutSine, so a scale that settles before the
        // landing does not read as a pop followed by a coast.
        seq.Insert(0f, rect.DOScale(Vector3.one * settings.holdScale, duration * 0.6f)
            .SetEase(Ease.OutBack, settings.burstSettleOvershoot));

        seq.OnComplete(delegate { StartIdleBob(rect, settings); });
    }

    private static Vector3 BurstOffset(int index, int count, CoinCollectSettings settings)
    {
        var angle = index * GoldenAngleDegrees * Mathf.Deg2Rad;

        // Equal-area steps between the inner and outer radius, so the coins spread evenly across the
        // cloud instead of clustering wherever a random radius happened to land twice. The cloud is
        // still dense — that is the intent — but its density is even and identical every run, rather
        // than randomly clumped in a different place each time.
        var inner = Mathf.Max(0f, settings.burstRadiusMin);
        var outer = Mathf.Max(inner + 1f, settings.burstRadiusMax);
        var t = (index + 0.5f) / count;
        var radius = Mathf.Sqrt(Mathf.Lerp(inner * inner, outer * outer, t));

        var vertical = Mathf.Sin(angle);

        // The two controls for where the cloud sits relative to the origin: this compresses its lower
        // half, and burstOffsetY lifts the whole thing. At the shipped values both are neutral, so the
        // cloud is centred on the origin and this line is a no-op — it is kept because it is the
        // control, not dead code. A lifted cloud was tried twice and rejected both times: these two
        // push the same way and stack, so the cloud gets away from the reward icon faster than either
        // number suggests, and the coins stop looking like they came out of the reward.
        if (vertical < 0f) vertical *= Mathf.Clamp01(settings.burstDownwardBias);

        // Squashed vertically because the cloud should be wider than it is tall — it sits on a portrait
        // screen, where there is least room directly above and below the reward.
        return new Vector3(
            Mathf.Cos(angle) * radius,
            vertical * radius * settings.burstVerticalSquash + settings.burstOffsetY,
            0f);
    }

    // ---------------------------------------------------------------------------------------------
    // Beat 2 — the hover
    // ---------------------------------------------------------------------------------------------

    // Runs for the whole hover and is killed by Stream. Deliberately an infinite loop rather than a
    // timed one: the hover length is a tuning value and the accelerating launch schedule stretches the
    // tail of it, so no fixed loop count would keep every coin bobbing until its own turn comes.
    private static void StartIdleBob(RectTransform rect, CoinCollectSettings settings)
    {
        if (settings.idleBobAmplitude <= 0f) return;

        // Scattered phase AND a slightly scattered period, which is a deliberate walk-back of an
        // earlier pass. That pass randomised the period widely and the cloud drifted in and out of
        // unison, which over overlapping sprites reads as jitter — so it was pinned to one shared
        // period. But a single period makes the cloud breathe as one body, and the reference is
        // explicit that it should shimmer and drift instead. A small variance is what gets both: the
        // coins never lock into unison, and they never diverge far enough to look unstable. Keep
        // idleBobPeriodVariance small; widen it and the old jitter comes straight back.
        var variance = Mathf.Clamp01(settings.idleBobPeriodVariance);
        var period = Mathf.Max(0.05f,
            settings.idleBobDuration * Random.Range(1f - variance, 1f + variance));

        rect.DOLocalMoveY(rect.localPosition.y + settings.idleBobAmplitude, period)
            .SetEase(Ease.InOutSine)
            .SetDelay(Random.Range(0f, period))
            .SetLoops(-1, LoopType.Yoyo);
    }

    // ---------------------------------------------------------------------------------------------
    // Beat 3 — the stream
    // ---------------------------------------------------------------------------------------------

    private static void ScheduleStream(
        List<Image> coins,
        Vector3 originLocal,
        Vector3 targetLocal,
        float arrivalScale,
        float lastBurstLaunch,
        CoinCollectSettings settings,
        UnityAction<int, int> onCoinArrived,
        UnityAction onAllCoinsComplete)
    {
        var total = coins.Count;

        // "The last coin has landed, then hold" — measured from the LAST bloom launch, not the first,
        // so the hold is a guarantee about EVERY coin rather than just the earliest. Passed in rather
        // than recomputed, because the bloom's schedule is neither even nor deterministic.
        //
        // holdDuration is allowed to be NEGATIVE, which is the one knob for overlapping the two beats:
        // the stream then starts while the last coins are still spreading out. Clamped so it can never
        // begin before the FIRST coin has finished blooming, because below that the stream would be
        // yanking coins that have not appeared yet — Stream kills a coin's bloom outright, so an
        // unclamped negative would leave coins jumping to the counter from mid-air or from nothing.
        var firstCoinLanded = lastBurstLaunch - settings.BurstSpan + settings.BurstDuration;
        var streamStart = Mathf.Max(firstCoinLanded,
            lastBurstLaunch + settings.BurstDuration + settings.holdDuration)/2f;

        // No jitter here, unlike the bloom. The stream's whole effect is beads evenly threaded on one
        // curve, and even spacing along the route is exactly what a jittered launch schedule destroys.
        // The bloom has no such constraint — its coins end up scattered anyway.
        var launchTimes = BuildLaunchSchedule(total, settings.streamSpan, settings.streamRateRamp, 0f);
        var soundOn = BuildSoundSchedule(launchTimes, settings);

        // ONE control point for every coin, computed from the burst origin rather than from each coin's
        // own resting spot. This is what makes the stream a ribbon: the coins leave from wherever they
        // happen to be sitting, but they all bend around the same point into the same target, so within
        // a fraction of a second they are threaded on one curve.
        //
        // The perpendicular is taken to the left of the flight direction, which for a reward in the
        // middle of the popup and a counter in the top-right corner sweeps the coins up and outward
        // before they bend back in — a bowed route rather than a straight diagonal. It is NOT
        // alternated by index: alternating scatters the stream and the ribbon is gone.
        var route = targetLocal - originLocal;
        var perpendicular = new Vector3(-route.y, route.x, 0f).normalized;
        var control = originLocal + route * 0.5f + perpendicular * settings.streamArc;

        var duration = Mathf.Max(0.01f, settings.streamDuration);
        var arrived = 0;

        for (var i = 0; i < total; i++)
        {
            var coin = coins[i];
            var index = i;

            UnityAction onArrived = delegate
            {
                arrived++;

                if (soundOn[index])
                    AudioManager.Instance.PlaySoundAction.Invoke(SoundName.CoinCollect);

                onCoinArrived?.Invoke(arrived, total);

                if (arrived == total)
                    onAllCoinsComplete?.Invoke();
            };

            DOVirtual.DelayedCall(streamStart + launchTimes[index], delegate
            {
                // Counted as arrived even if the coin is already gone, so a destroyed coin cannot
                // strand onAllCoinsComplete — the caller's scene flow hangs on that callback.
                if (coin == null)
                {
                    onArrived.Invoke();
                    return;
                }

                Stream(coin, control, targetLocal, arrivalScale, duration, onArrived);
            });
        }
    }

    // Launch times relative to the first departure, shared by BOTH the bloom and the stream — that
    // sharing is deliberate, and is most of why the two halves feel like one motion rather than two
    // animations joined by a pause.
    //
    // The rate ACCELERATES: gaps start wide, so the opening coins are clearly separated and readable
    // one at a time, and close up into a dense pour with the most coins moving at the very end.
    //
    // `span` is the ABSOLUTE window from the first launch to the last, not a per-coin gap, so the coin
    // count changes only how densely the window is filled. That is deliberate and is what makes a
    // 50-coin reward possible: built from a per-coin gap, doubling the count doubled the animation, and
    // the gap that suited 16 coins would have run a 2.4s bloom at 50. `ramp` redistributes the gaps
    // within the window — an exponent below 1 grows the elapsed time quickly at first and then slowly,
    // which is the same thing as gaps that start wide and close up. At 1 the schedule is perfectly even.
    //
    // Launch to launch, NOT landing to launch: trips overlap heavily by design — that overlap IS the
    // ribbon in the stream and the bloom in the burst, and waiting for each coin to land before
    // releasing the next would leave a single coin on screen at a time.
    private static float[] BuildLaunchSchedule(int total, float span, float ramp, float jitter)
    {
        var times = new float[total];
        if (total <= 1) return times;

        var safeSpan = Mathf.Max(0f, span);
        var safeRamp = Mathf.Max(0.05f, ramp);

        for (var i = 0; i < total; i++)
            times[i] = safeSpan * Mathf.Pow(i / (float)(total - 1), safeRamp);

        // Clamped rather than trusted: at 0.5 and above a coin can cross its neighbour's slot, and
        // since draw order follows appearance order that would put later coins underneath earlier ones.
        var safeJitter = Mathf.Clamp(jitter, 0f, 0.49f);
        if (safeJitter <= 0f) return times;

        // Each launch is nudged by a fraction of its OWN gap, so a schedule whose gaps span an order of
        // magnitude gets proportionate scatter rather than one absolute wobble that would be invisible
        // at the sparse end and reorder the dense one.
        for (var i = 1; i < total; i++)
        {
            var gap = times[i] - times[i - 1];
            times[i] += Random.Range(-gap * safeJitter, gap * safeJitter);
        }

        return times;
    }

    // Thins the ticks out where the stream is denser than the ear can separate. Computed from the
    // ACTUAL launch times rather than a fixed every-nth-coin stride, because the schedule above is not
    // even: a stride tuned for the trickle at the start would smear into one noise at the dense end,
    // and a stride tuned for the dense end would drop most of the opening ticks. Arrival order matches
    // launch order and every trip is the same length, so launch spacing is arrival spacing.
    private static bool[] BuildSoundSchedule(float[] launchTimes, CoinCollectSettings settings)
    {
        var soundOn = new bool[launchTimes.Length];
        var lastSound = float.NegativeInfinity;
        var stride = Mathf.Max(1, settings.soundEveryNthCoin);

        for (var i = 0; i < launchTimes.Length; i++)
        {
            if (i % stride != 0) continue;
            if (launchTimes[i] - lastSound < settings.minSoundInterval) continue;

            soundOn[i] = true;
            lastSound = launchTimes[i];
        }

        return soundOn;
    }

    private static void Stream(Image coin, Vector3 control, Vector3 targetLocal, float arrivalScale,
        float duration, UnityAction onArrived)
    {
        var rect = coin.rectTransform;

        // Kills whatever is still running on this transform. At the shipped values that is the idle
        // bob — an infinite Yoyo on localPosition, which would otherwise fight the flight tween over y
        // for the whole trip. But holdDuration can be made negative to overlap the two beats, and then
        // a late coin is still mid-bloom with its move and scale live too, so this has to take
        // everything rather than just the bob. It reaches the bloom sequence because that sequence is
        // SetTarget(rect).
        //
        // DOKill without complete:true on purpose: completing the bloom would snap the coin out to its
        // resting spot first, and it is also what fires StartIdleBob, which would put a fresh infinite
        // bob on a coin that is leaving.
        rect.DOKill();

        // Deliberately NO restacking here. AssignDrawOrder already put later coins above earlier ones,
        // which is the correct stacking for the overlapping stream as well as for the resting cloud —
        // and touching sortingOrder mid-flight would dirty the coin's canvas for nothing. Lifting each
        // departing coin to one shared "top" value would actively reintroduce the flicker, since dozens
        // of coins are in the air together at the peak.
        SetCoinParticles(coin, true);

        // Shrinks over the flight so it arrives at the counter icon's own size and slots in cleanly.
        rect.DOScale(Vector3.one * arrivalScale, duration).SetEase(Ease.Linear);

        // Waypoints only — NOT the coin's current position first. A path whose first waypoint is where
        // the coin already stands gives CatmullRom a zero-length opening segment, and the kink that
        // produces was a visible flick at the start of every flight.
        //
        // LINEAR, and this one is not a matter of taste, which is why it is not exposed. Evenly spaced
        // beads on a string require every coin to be the same distance along the route at the same
        // offset from its launch, which is only true at constant speed. An eased flight bunches the
        // coins at one end of the ribbon and stretches the gaps at the other, and the effect stops
        // reading as a chain.
        rect.DOLocalPath(new[] { control, targetLocal }, duration, PathType.CatmullRom)
            .SetEase(Ease.Linear)
            .SetOptions(false)
            .OnComplete(delegate
            {
                // Gone the instant it lands — no fade, no shrink-out. The counter's own punch is what
                // acknowledges the arrival; anything lingering here reads as the coin bouncing off.
                Object.Destroy(coin.gameObject);
                onArrived?.Invoke();
            });
    }

    private static void SetCoinParticles(Image coin, bool isPlaying)
    {
        var particleList = coin.GetComponentsInChildren<ParticleSystem>(true);

        foreach (var particle in particleList)
        {
            if (isPlaying)
            {
                particle.Clear();
                particle.Play();
                continue;
            }

            particle.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
            particle.Clear();
        }
    }
}
