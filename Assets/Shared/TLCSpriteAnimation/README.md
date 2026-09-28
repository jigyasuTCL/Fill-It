# TLC Sprite Animation

Sprite-sheet animation for Unity, driven by ScriptableObject clips instead of the Animator.
Built for 2D characters whose art arrives as TexturePacker atlases or even grids.

Drop-in: copy the whole `TLCSpriteAnimation` folder into any project's `Assets/`. No packages,
no dependencies, no assembly definitions, nothing to wire up. The `Editor` folder is named
`Editor`, so Unity keeps the tooling out of builds automatically.

## Why not Unity's Animator

An Animator controller per character state is a lot of asset plumbing for "swap these sprites at
this rate", and it does not carry the two things this does: per-clip rest scheduling, and a size
correction between padded sheet frames and an unpadded static sprite. If you need blend trees or
root motion, use the Animator. If you need a character to idle, react, and hold a pose, this is
smaller.

## The workflow

1. **Slice the sheet.**
   - TexturePacker: select the `.tpsheet` (or its folder, or the `.png` beside it) and run
     *Assets ▸ Sprite Animation ▸ Slice Sheet (TexturePacker)*.
   - Even grid: select the texture and run *Assets ▸ Sprite Animation ▸ Slice Sheet (even grid)…*,
     then enter columns and rows.
2. **Make the clip.** Select the sliced sheet (or a folder of them) and run
   *Assets ▸ Sprite Animation ▸ Create Sprite Animation from Sheet*. You get a
   `SpriteAnimationSO` asset beside the sheet, with the same name. Re-running updates it in place,
   so nothing referencing it is ever orphaned.
3. **Play it.** Put a `SpriteAnimator` on the object holding your `SpriteRenderer` and call
   `Play(clip)`.

```csharp
using TLC.SpriteAnimation;

[SerializeField] private SpriteAnimator animator;
[SerializeField] private SpriteAnimationSO idle;
[SerializeField] private Sprite staticIcon;

void Start()
{
    // Declare the size every clip should match BEFORE playing — see "Art fit" below.
    animator.SetSizeReference(staticIcon);
    animator.Play(idle);
}
```

## Clip settings

Everything lives on the clip asset; the animator component has no tuning of its own beyond the
renderer it drives. Three playback shapes:

| Setting | Behaviour |
|---|---|
| *(neither box ticked)* | Hold frame 0, play once, rest a random delay from `restBetweenPlays`, repeat. The idle default. The random rest is what stops two characters on screen blinking in lockstep. |
| `playContinuously` | Loop forever, no pauses. |
| `holdLastFrame` | Play up to `holdFrameIndex` and **stay** there. For poses that settle — a look-at that turns and keeps looking. |

`holdFrameIndex` matters when a sheet animates out **and back**. If the final frame returns to
neutral, holding it looks identical to idle and the pose reads as never having happened. Point it
at the frame where the pose is fullest.

### Pose-to-pose transitions

Swapping one `holdLastFrame` clip for another is not a cut. The current pose first plays
**backwards** to neutral, then the new one runs forwards, so a head sweeps through centre instead
of snapping. Author look-at sheets neutral-to-turned and reversing them is exactly the return
motion. Interrupting mid-turn reverses from wherever it is, so rapid direction changes do not
stutter. `transitionSpeedMultiplier` controls how snappy that return is.

## Art fit

The one non-obvious part, and the reason clips carry `artFitMultiplier`.

Sheet frames are padded; a standalone static sprite is not. Matching the two sprite *rects*
therefore draws the animated character noticeably smaller than the static one — in the case this
was built for, 43% of its frame against 77% of the static sprite, so it rendered at 56% size.

The importer measures the real opaque pixel bounds of both and bakes the ratio into the clip. It
finds the static counterpart **by naming convention**: the sheet's prefix, up to the first
underscore. `hero_idle.png` looks for a single sprite named `hero`. Multi-sprite textures are
skipped, so another sheet with a matching name is never mistaken for the static art.

No static counterpart is a perfectly normal setup — you just get no correction (`1`), reported at
Log level, and you can set the multiplier by hand.

Measurement goes through a `Graphics.Blit` to a temporary RenderTexture rather than toggling the
importer's Read/Write flag. Toggling costs a full reimport each way, and an interrupted run would
leave Read/Write enabled and quietly bloat the build.

## Frame naming

Both slicers name frames `{sheet}_{n}`, and the clip builder sorts by that trailing number
*numerically*. A plain string sort would run 0, 1, 10, 11, 2 — which looks like broken animation
rather than a sorting bug.

For TexturePacker sheets, playback order comes from the trailing number in each frame's own name
(`frame_007` → 7), not from row order in the file: TexturePacker writes rows in packing order,
which has nothing to do with the animation.

## Gotchas worth knowing

- **TexturePacker: use the Unity `.tpsheet` exporter, not the generic XML/plist.** Sprite rects in
  Unity are axis-aligned with nowhere to record a rotation, so the Unity exporter never rotates
  frames. The generic exporters do, and those sheets cannot be sliced correctly here.
- **The `.png` and `.tpsheet` must share a filename and sit in the same folder.** The sibling png
  deliberately wins over the `:texture=` line inside the file, so renaming the pair does not
  require hand-editing the data file.
- **Re-export the pair together.** If the atlas is re-exported at a different size, the old rects
  still land inside the texture and the frame count still looks right — the sheet just slices into
  visibly wrong pieces. The slicer checks the declared `:size=` against the actual texture and
  refuses rather than producing that.
- **The even-grid slicer rounds cell edges, not cell size.** Unity's built-in "Grid By Cell Count"
  truncates to one integer cell size and drifts — ~3.6px by the last column of a 1024px/5 sheet,
  which reads as the character sliding sideways as it animates.
- **`SpriteAnimator` writes to the renderer's transform scale.** If something else also owns that
  scale, hand it `SetBaseScale` or the two will fight. Cloning a mid-animation object needs
  `SetBaseScale(original.BaseScale)` on the clone, otherwise it normalises a second time.
- **Keep clips out of `Resources`.** Unity resolves an asset's whole reference graph on load, so
  clips hung off an always-loaded registry make every frame of every character resident at once.

## Files

```
TLCSpriteAnimation/
  Runtime/
    SpriteAnimationSO.cs      clip data — frames, rate, scheduling, art fit
    SpriteAnimator.cs         plays a clip on a SpriteRenderer
  Editor/
    TpSheetSlicer.cs          .tpsheet (TexturePacker) → sliced sprites
    SpriteSheetSlicer.cs      even grid → sliced sprites
    SpriteSheetToAnimation.cs sliced sheet → SpriteAnimationSO, with art-fit measurement
```

Namespaces are `TLC.SpriteAnimation` and `TLC.SpriteAnimation.EditorTools`.
