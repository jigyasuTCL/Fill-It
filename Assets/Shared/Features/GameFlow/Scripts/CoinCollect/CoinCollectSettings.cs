using UnityEngine;

// Every number the level-complete coin collect uses, in one asset a designer can tune without touching
// code. CoinCollectAnimator reads it and nothing else does.
//
// HOW IT REACHES THE ANIMATION: wired as a direct object reference on LevelCompleteUI, so it is a hard
// dependency of the LevelComplete prefab and Unity pulls it into the build automatically. There is no
// runtime lookup on the normal path. The Instance property below is only a safety net for a reference
// left unassigned; see it for why that net exists at all.
//
// WHY AN ASSET AND NOT SERIALIZED FIELDS ON THE POPUP. An earlier pass put this on LevelCompleteUI as a
// serialized settings class, and that had to be pulled back out: a serialized field silently shadows
// the C# initialiser it was created from, so the first time anyone opened and saved LevelComplete.prefab
// the whole animation froze at whatever was in the inspector that day, and every later change in code
// did nothing while still looking correct in the diff. A single named asset does not have that problem
// — there is exactly one of it, it is obvious where the live values are, and it is the same shape the
// rest of the framework already uses for tuning (GamePlayDataSO, FillTheNodeConfig).
//
// The field initialisers below are the tuned values, so a freshly created asset ships the intended
// look, and CoinCollectAnimator falls back to them if the asset is missing rather than throwing.
//
// FOUR BEATS, and the headers below follow them in order:
//   0. BEAT    a quarter second where nothing moves at all — what turns the collect into a payoff
//              rather than motion continuing from the popup's entry.
//   1. BLOOM   coins appear one at a time at the reward icon, growing from nothing, and spread out
//              into a loose cloud centred on it.
//   2. HOVER   the cloud holds, every coin bobbing on its own phase and a slightly different period.
//   3. STREAM  every coin flies the SAME curved route into the counter, offset only in time, so they
//              read as one continuous ribbon. The launch rate accelerates as it goes.
//
// Two things are deliberately NOT tunable here, because both are structural rather than taste:
// the coins never rotate (a rotating coin is a completely different effect), and the stream's flight
// is linear (evenly spaced beads on a string require constant speed). See CoinCollectAnimator.
[CreateAssetMenu(fileName = nameof(CoinCollectSettings), menuName = "TLC/CoinCollectSettings")]
public class CoinCollectSettings : ScriptableObject
{
    private static CoinCollectSettings _instance;

    // NOT the normal route — the asset is wired directly onto LevelCompleteUI, and that is the path
    // that runs. This is the fallback for a reference someone forgot to assign, kept because the
    // collect animation is the last thing standing between a level win and the next scene loading, so
    // an unwired inspector field must not be able to stop the game. It is also what the TLC > SO menu
    // item uses to find the asset. Anything reaching this in a build is a bug to fix, not a design.
    public static CoinCollectSettings Instance
    {
        get
        {
            if (_instance) return _instance;

            _instance = Resources.Load<CoinCollectSettings>($"Configs/{nameof(CoinCollectSettings)}");

            // A warning, not an error, and then a throwaway instance carrying the defaults below —
            // because the animation is the last thing standing between a level win and the next scene
            // loading, and a missing asset must not be able to stop the game. Log it loudly enough to
            // be fixed, then carry on looking correct.
            if (!_instance)
            {
                Debug.LogWarning($"{nameof(CoinCollectSettings)} asset not found at Resources/Configs — "
                                 + "falling back to the built-in defaults. Create one via "
                                 + "Assets > Create > TLC > CoinCollectSettings and put it in Resources/Configs.");
                _instance = CreateInstance<CoinCollectSettings>();
            }

            return _instance;
        }
    }

    [Header("Count — how much reward one coin is worth")]
    [Tooltip("Coins shown = reward / this, capped by Max Coins. Set the two together: a cap low enough "
             + "to clamp both the normal and the doubled reward makes the doubling invisible. At 2 and "
             + "50, a 50 reward shows 25 coins and the 100 x2 reward shows 50.")]
    public int currencyPerCoin = 2;

    [Tooltip("Ceiling on the coin count. This does NOT change how long the animation takes — both "
             + "schedules are built from fixed spans, so more coins means a denser cloud and a denser "
             + "ribbon, not a longer one. It is a cost dial: every coin is an Image with its own "
             + "Canvas and two particle systems. Halve it first if the popup ever hitches.")]
    public int maxCoins = 50;

    [Header("Reward number")]
    [Tooltip("Whether the popup's reward number counts down as the coins appear, one step per coin, "
             + "reaching zero as the last one leaves.\n\n"
             + "Off leaves it sitting at its full value for the whole animation, which also reads "
             + "perfectly well as \"you won 50\" — which of the two is better is a design call, so it "
             + "is a switch rather than a decision.\n\n"
             + "Note this is tied to the coins APPEARING, not to them landing in the counter: they "
             + "visibly come out of this number, so it is spent as they leave it. The wallet counter "
             + "at the top is the one that fills as they arrive, and it is not affected by this.")]
    public bool drainRewardNumberOnSpawn = true;

    [Header("Beat 0 — the pause before anything moves")]
    [Tooltip("Dead time after the button is tapped. Short, but it is what separates the collect from "
             + "the popup's own entry animation.")]
    public float startDelay = 0.1f;

    [Header("Beat 1 — the bloom (coins appearing and spreading out)")]
    [Tooltip("How much SLOWER the bloom runs than the stream. Both the bloom's launch window and each "
             + "coin's trip out are this multiple of the stream's equivalents, so the two halves stay "
             + "in proportion automatically and retuning the stream carries the bloom with it.\n\n"
             + "Keep it at or above 1. Above 1 the cut builds — a deliberate bloom, then a faster pour. "
             + "Below 1 the bloom outruns the stream and they stop reading as one motion. This is the "
             + "most expensive single value here for total runtime.")]
    public float burstPaceScale = 1.7f;

    [Tooltip("How hard the appearance rate accelerates. Below 1 the gaps start wide and close up, so "
             + "the first coins appear clearly one at a time and the rest bloom outward at a rising "
             + "rate. At 1 they appear at a perfectly even rate, which at these speeds reads as all of "
             + "them arriving at once.")]
    public float burstRateRamp = 0.45f;

    [Tooltip("Randomises each appearance by this fraction of its own gap, so the bloom is not "
             + "metronomic. MUST stay below 0.5: past that a coin can cross its neighbour's slot, and "
             + "since draw order follows appearance order that would put later coins underneath "
             + "earlier ones.")]
    [Range(0f, 0.49f)]
    public float burstStaggerJitter = 0.25f;

    [Tooltip("Size a coin starts at. Zero means it does not exist on screen until its own turn, which "
             + "is what makes the bloom read as coins arriving one at a time rather than as a set that "
             + "was already there beginning to move.")]
    public float burstSpawnScale;

    [Tooltip("Resting size in the cloud, as a multiple of the coin art's authored size. 1 keeps the "
             + "dense overlapping cloud; lower it to read as separate coins.")]
    public float holdScale = 1f;

    [Tooltip("How far a coin's path bows outward, as a fraction of ITS OWN travel distance. "
             + "Proportional on purpose: a fixed height sends the short-travel coins on huge arcs well "
             + "clear of the cloud, which reads as them being dropped in from above rather than "
             + "spreading out from the centre.")]
    public float burstArcFactor = 0.25f;

    [Tooltip("Floor and ceiling on the bow, in canvas units, so neither the innermost nor the "
             + "outermost coin gets an extreme one.")]
    public float burstArcMin = 18f;

    [Tooltip("See Burst Arc Min.")]
    public float burstArcMax = 60f;

    [Tooltip("Random variation on each coin's bow, as a fraction. Identical bows over an even spread "
             + "read as a mechanical fountain.")]
    public float burstArcVariance = 0.3f;

    [Tooltip("How far past full size a coin scales before settling back, as a DOTween OutBack "
             + "overshoot. The coin's arrival lives entirely in this: there is deliberately no "
             + "overshoot on its position, which read as a bounce.")]
    public float burstSettleOvershoot = 0.5f;

    [Header("Beat 1 — the cloud (where the coins come to rest)")]
    [Tooltip("Inner and outer edge of the cloud, in canvas units. The coins are larger than their "
             + "share of it on purpose — the cloud is meant to overlap — so widening this band is how "
             + "you loosen it.")]
    public float burstRadiusMin = 100f;

    [Tooltip("See Burst Radius Min.")]
    public float burstRadiusMax = 215f;

    [Tooltip("Below 1 flattens the cloud into an ellipse wider than it is tall, which suits a portrait "
             + "screen where there is least room directly above and below the reward.")]
    public float burstVerticalSquash = 0.7f;

    [Tooltip("How much of the cloud sits BELOW the origin, as a fraction of how much sits above it. "
             + "Together with Burst Offset Y this decides where the cloud sits relative to the coins' "
             + "starting point. 1 and 0 give a cloud centred squarely on it, which is what is wanted "
             + "here — both values push the same way and stack, so a lifted cloud gets away from the "
             + "reward icon faster than either number suggests, and the coins stop looking like they "
             + "came out of the reward.")]
    public float burstDownwardBias = 1f;

    [Tooltip("Lifts the whole cloud off the origin. Applies to the RESTING positions only — the coins "
             + "still appear at the origin point itself. See Burst Downward Bias.")]
    public float burstOffsetY;

    [Header("Beat 2 — the hover")]
    [Tooltip("THIS IS THE DELAY BEFORE THE FIRST COIN HEADS FOR THE COUNTER. Seconds between the last "
             + "coin landing in the cloud and the first coin setting off.\n\n"
             + "It may be NEGATIVE, which starts the stream before the bloom has finished: at -0.2 the "
             + "first coins are already on their way while the last few are still spreading out. That "
             + "overlaps the two beats into one continuous motion, at the cost of the cloud never "
             + "existing whole. Pushed far enough negative it is clamped so the stream can never begin "
             + "before the first coin has landed.\n\n"
             + "Note it is the wait for the LAST coin only. The first coin to land waits this PLUS the "
             + "entire rest of the bloom, so it is a weak lever on how long the cloud appears to hover "
             + "— for that, Burst Pace Scale is the value that moves it.")]
    public float holdDuration;

    [Tooltip("How far each coin bobs while hovering, in canvas units. The coins overlap, so this is "
             + "relative motion between overlapping sprites — keep it well under a coin's height or "
             + "they visibly trade places and the cloud churns. Zero does NOT stop the bob.")]
    public float idleBobAmplitude = 9f;

    [Tooltip("One full bob, in seconds.")]
    public float idleBobDuration = 0.5f;

    [Tooltip("How much each coin's bob period may differ from the value above, as a fraction. Small on "
             + "purpose: enough that the cloud shimmers rather than breathing as one body, not enough "
             + "that it reads as jitter.")]
    public float idleBobPeriodVariance = 0.14f;

    [Header("Beat 3 — the stream (the ribbon into the counter)")]
    [Tooltip("How long the stream spends launching, first coin to last. An absolute window, NOT a "
             + "per-coin gap — so the coin count changes only how densely it is filled, never how long "
             + "the animation runs.")]
    public float streamSpan = 0.45f;

    [Tooltip("How long one coin's trip to the counter takes. Far longer than the gaps within the span "
             + "above, which is the point: many coins in the air at once is what turns separate "
             + "departures into one ribbon. Retune it together with Stream Span — changing either "
             + "alone thins the ribbon out into separate coins.")]
    public float streamDuration = 0.3f;

    [Tooltip("How hard the launch rate accelerates — a trickle that builds into a dense pour. Below 1 "
             + "the gaps start wide and close up; at 1 the stream is perfectly even.")]
    public float streamRateRamp = 0.45f;

    [Tooltip("How far the shared route bows out from the straight line between the reward and the "
             + "counter. Generous on purpose: the coins should sweep up and outward before bending in, "
             + "and a shallow value just reads as a slightly wobbly diagonal. Every coin uses the same "
             + "bow — that is what makes the stream a ribbon rather than a scatter.")]
    public float streamArc = 110f;

    [Header("Particles & sound")]
    [Tooltip("Whether the coin prefab's emitters run during the bloom and hover as well as the stream. "
             + "Off by default: they are a flight trail, and running two per coin across an overlapping "
             + "cloud for the whole hover is a shimmer that reads as the cloud jittering.")]
    public bool playParticlesOnBurst;

    [Tooltip("Only every Nth arrival is allowed to tick. 1 means all of them are, and Min Sound "
             + "Interval then thins them where the stream is denser than the ear can separate.")]
    public int soundEveryNthCoin = 1;

    [Tooltip("Shortest gap between two coin ticks, in seconds. Stops the dense end of the stream "
             + "smearing into one noise.")]
    public float minSoundInterval = 0.06f;

    // --- Derived ----------------------------------------------------------------------------------
    // Not fields, so they cannot drift out of proportion with the stream. This is the relationship
    // burstPaceScale exists to express, and it is the reason the two halves of the cut read as one
    // motion — see that field's tooltip.

    /// <summary>The bloom's launch window, first coin appearing to last.</summary>
    public float BurstSpan => streamSpan * burstPaceScale;

    /// <summary>One coin's trip out from the origin to its place in the cloud.</summary>
    public float BurstDuration => streamDuration * burstPaceScale;
}
