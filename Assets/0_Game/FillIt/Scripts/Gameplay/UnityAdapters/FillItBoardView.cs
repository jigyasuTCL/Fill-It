using System;
using System.Collections;
using System.Collections.Generic;
using FillIt.Views;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.Controls;

namespace FillIt.UnityAdapters
{
    /// <summary>
    /// World-space Fillomino board: lays the cell grid out inside a fixed board width, turns
    /// pointer/touch input into cell events (press, drag across cells, release), and plays the
    /// board-wide animations (enter wave, drag trail, FTUE guide path, win pulse, confetti).
    /// Knows nothing about rules - FillItGameplayManager decides what each gesture does.
    /// </summary>
    public sealed class FillItBoardView : MonoBehaviour, IFillItBoardView
    {
        [SerializeField] private Transform cellContainer;
        [SerializeField] private FillItCellView cellPrefab;
        [SerializeField] private Camera inputCamera;

        [Header("Layout")]
        // The board background ("Base", BoardRoot's first child if left unassigned) - scaled to fit
        // the grid. BoardRoot itself is never scaled (the FTUE slide moves it).
        [SerializeField] private Transform baseTransform;
        [Tooltip("World width the whole grid fits into, whatever its size.")]
        [SerializeField] private float boardWorldWidth = 4.3f;
        [SerializeField] private float boardCenterYOffset = 0f;
        [Tooltip("Tile size as a fraction of the cell spacing - the rest is the gap between tiles.")]
        [Range(0.5f, 1f)] [SerializeField] private float cellFillPercent = 0.9f;
        [Tooltip("Base background world size = grid width + this padding (0 disables base resizing).")]
        [SerializeField] private float basePadding = 0.3f;

        [Header("Input")]
        [Tooltip("A press this far outside the grid (in cells) still grabs the nearest edge cell.")]
        [SerializeField] private float edgeGraceCells = 0.15f;

        [Header("Board Enter Animation")]
        [SerializeField] private float enterCellDuration = 0.22f;
        [SerializeField] private float enterCellStagger = 0.025f;

        [Header("Region Complete")]
        [SerializeField] private float regionCompleteStagger = 0.035f;

        [Header("Drag Trail")]
        [SerializeField] private LineRenderer dragTrail;
        [SerializeField] private float dragTrailWidth = 0.14f;
        [SerializeField] private Color dragTrailTint = new Color(0.25f, 0.45f, 0.9f, 0.55f);

        [Header("FTUE Guide Path")]
        [SerializeField] private LineRenderer guidePath;
        [SerializeField] private Color guidePathColor = new Color(1f, 0.62f, 0.24f, 0.9f);
        [SerializeField] private float guidePathWidth = 0.1f;

        [Header("Heart Break FX (Assets/Shared/Features/LevelMistakeLimit) - plays above the mistaken cell")]
        [SerializeField] private GameObject heartBreakPrefab;
        [SerializeField] private float heartBreakLifetimeSeconds = 1f;
        [SerializeField] private float heartBreakVerticalOffset = 1f;

        [Header("Level Win Board Animation")]
        [SerializeField] private float winPulseCellDuration = 0.6f;
        [SerializeField] private float winPulseCellStagger = 0.05f;
        [SerializeField] private float winPulseScaleMultiplier = 1.25f;
        [SerializeField] private ParticleSystem leftWinConfetti;
        [SerializeField] private ParticleSystem rightWinConfetti;

        private readonly List<FillItCellView> cells = new List<FillItCellView>();
        private readonly List<Vector3> linePointsScratch = new List<Vector3>();
        private Vector3? baseOriginalScale;
        private Vector2 baseSpriteSize = Vector2.zero;
        private int size;
        private float spacing;
        private Vector2 origin;
        private int hintIndex = -1;
        private bool pointerDown;
        private int pointerId = -1;
        private int lastPointerCell = -1;
        private Vector2 lastPointerLocal;
        private float guidePathTime;
        private int[] guidePathCells;

        /// <summary>Press landed on a cell - returns true if the gesture was accepted.</summary>
        public Func<int, bool> CellPressed;
        public event Action<int> CellDragEntered = delegate { };
        public event Action CellReleased = delegate { };

        public Camera InputCamera => inputCamera != null ? inputCamera : Camera.main;
        public int Size => size;
        public int CellCount => size * size;

        private void Awake()
        {
            if (inputCamera == null)
            {
                inputCamera = Camera.main;
            }

            dragTrail = EnsureLine(dragTrail, "DragTrail", 0);
            guidePath = EnsureLine(guidePath, "GuidePath", 1);
            dragTrail.enabled = false;
            guidePath.enabled = false;
        }

        private void Update()
        {
            AnimateGuidePath();

            if (!Shared.GameFlow.InputManager.GamePlayInputEnabled)
            {
                if (pointerDown)
                {
                    ReleasePointer();
                }

                return;
            }

            if (!pointerDown)
            {
                if (TryGetPointerPressed(out Vector2 pressScreen, out int pressId) && !IsOverUi(pressScreen))
                {
                    int cell = ScreenToCell(pressScreen, edgeGraceCells, out Vector2 local);
                    if (cell >= 0 && (CellPressed == null || CellPressed(cell)))
                    {
                        pointerDown = true;
                        pointerId = pressId;
                        lastPointerCell = cell;
                        lastPointerLocal = local;
                    }
                }

                return;
            }

            if (TryGetPointerPosition(pointerId, out Vector2 screen, out bool released))
            {
                StepPointerTo(screen);
            }

            if (released)
            {
                ReleasePointer();
            }
        }

        public void Build(int boardSize)
        {
            size = Mathf.Max(1, boardSize);
            int required = size * size;
            EnsureCellCount(required);

            spacing = boardWorldWidth / size;
            origin = new Vector2(-boardWorldWidth * 0.5f + spacing * 0.5f, boardCenterYOffset + boardWorldWidth * 0.5f - spacing * 0.5f);
            float baseCellSize = GetBaseCellWorldSize();
            float cellScale = spacing * cellFillPercent / Mathf.Max(0.0001f, baseCellSize);
            float spacingInCellUnits = spacing / Mathf.Max(0.0001f, cellScale);

            for (int index = 0; index < cells.Count; index++)
            {
                bool active = index < required;
                cells[index].gameObject.SetActive(active);
                if (!active)
                {
                    continue;
                }

                int row = index / size;
                int column = index % size;
                Transform cellTransform = cells[index].transform;
                cellTransform.localScale = new Vector3(cellScale, cellScale, 1f);
                cellTransform.localPosition = new Vector3(origin.x + column * spacing, origin.y - row * spacing, 0f);
                cells[index].Bind(index, spacingInCellUnits);
            }

            FitBase();
            hintIndex = -1;
            ClearDragTrail();
            ClearGuidePath();
        }

        public void SetCell(int index, FillItCellVisualState state, bool animateValueChange)
        {
            if (index < 0 || index >= cells.Count)
            {
                return;
            }

            cells[index].SetState(state, animateValueChange);
        }

        public void SetDragTrail(IReadOnlyList<Vector2Int> segments, int trailValue)
        {
            if (dragTrail == null || segments == null || segments.Count == 0)
            {
                ClearDragTrail();
                return;
            }

            // Segments arrive in join order, each starting where an earlier one ended (a tree, not
            // always a single line) - draw them as one polyline, hopping back when a branch starts
            // somewhere other than the previous end.
            linePointsScratch.Clear();
            int lastEnd = -1;
            for (int index = 0; index < segments.Count; index++)
            {
                Vector2Int segment = segments[index];
                if (segment.x != lastEnd)
                {
                    linePointsScratch.Add(CellLocalCenter(segment.x));
                }

                linePointsScratch.Add(CellLocalCenter(segment.y));
                lastEnd = segment.y;
            }

            Color color = cellPrefab != null ? cellPrefab.GetNumberColor(trailValue) : Color.white;
            color = Color.Lerp(color, dragTrailTint, 0.45f);
            color.a = dragTrailTint.a;
            dragTrail.startColor = color;
            dragTrail.endColor = color;
            dragTrail.widthMultiplier = dragTrailWidth * spacing;
            dragTrail.positionCount = linePointsScratch.Count;
            dragTrail.SetPositions(linePointsScratch.ToArray());
            dragTrail.enabled = true;
        }

        public void ClearDragTrail()
        {
            if (dragTrail != null)
            {
                dragTrail.enabled = false;
                dragTrail.positionCount = 0;
            }
        }

        /// <summary>FTUE: a pulsing line along the cells the player should drag through (one cell = no line).</summary>
        public void ShowGuidePath(int[] path)
        {
            guidePathCells = path;
            guidePathTime = 0f;
            if (guidePath == null || path == null || path.Length < 2)
            {
                if (guidePath != null)
                {
                    guidePath.enabled = false;
                }

                return;
            }

            linePointsScratch.Clear();
            for (int index = 0; index < path.Length; index++)
            {
                linePointsScratch.Add(CellLocalCenter(path[index]));
            }

            guidePath.widthMultiplier = guidePathWidth * spacing;
            guidePath.positionCount = linePointsScratch.Count;
            guidePath.SetPositions(linePointsScratch.ToArray());
            guidePath.enabled = true;
        }

        public void ClearGuidePath()
        {
            guidePathCells = null;
            if (guidePath != null)
            {
                guidePath.enabled = false;
                guidePath.positionCount = 0;
            }
        }

        public void PlayRegionComplete(IReadOnlyList<int> regionCells)
        {
            if (regionCells == null)
            {
                return;
            }

            for (int index = 0; index < regionCells.Count; index++)
            {
                int cell = regionCells[index];
                if (cell >= 0 && cell < cells.Count)
                {
                    cells[cell].PlayRegionComplete(index * regionCompleteStagger);
                }
            }
        }

        public void PlayMistake(IReadOnlyList<int> mistakeCells)
        {
            if (mistakeCells == null)
            {
                return;
            }

            for (int index = 0; index < mistakeCells.Count; index++)
            {
                int cell = mistakeCells[index];
                if (cell >= 0 && cell < cells.Count)
                {
                    cells[cell].PlayMistakeShake();
                }
            }
        }

        public void PlayLockedTapShake(int index)
        {
            if (index >= 0 && index < cells.Count)
            {
                cells[index].PlayMistakeShake();
            }
        }

        // Plays just above the cell where the mistake happened, so it reads as tied to it.
        public void PlayHeartBreak(int index)
        {
            if (heartBreakPrefab == null)
            {
                return;
            }

            Transform anchor = index >= 0 && index < cells.Count ? cells[index].transform : cellContainer;
            if (anchor == null)
            {
                return;
            }

            Vector3 spawnPosition = anchor.position + new Vector3(0f, heartBreakVerticalOffset, -1f);
            GameObject instance = Instantiate(heartBreakPrefab, spawnPosition, heartBreakPrefab.transform.rotation);
            Destroy(instance, heartBreakLifetimeSeconds);
        }

        public void PlayHintParticle(int index)
        {
            if (index >= 0 && index < cells.Count)
            {
                cells[index].PlayHintParticle();
            }
        }

        public void ShowHintTarget(int index, int value)
        {
            ClearHintTarget();
            if (index < 0 || index >= cells.Count)
            {
                return;
            }

            hintIndex = index;
            cells[index].ShowHintTarget(value);
        }

        public void ClearHintTarget()
        {
            if (hintIndex >= 0 && hintIndex < cells.Count)
            {
                cells[hintIndex].ClearHintTarget();
            }

            hintIndex = -1;
        }

        public void Clear()
        {
            ReleasePointer();
            ClearHintTarget();
            ClearDragTrail();
            ClearGuidePath();
            size = 0;
            for (int index = 0; index < cells.Count; index++)
            {
                if (cells[index] != null)
                {
                    cells[index].gameObject.SetActive(false);
                }
            }
        }

        // ----- level start / win animations -----

        /// <summary>Tiles pop in one-by-one in a wave from the top-left corner.</summary>
        public void PlayEnterAnimation(Action onComplete = null)
        {
            StartCoroutine(EnterAnimationRoutine(onComplete));
        }

        private IEnumerator EnterAnimationRoutine(Action onComplete)
        {
            global::AudioManager.Instance.PlaySoundAction?.Invoke(global::SoundName.BoardTilesIntro);
            int count = Mathf.Min(CellCount, cells.Count);
            float maxDelay = 0f;
            for (int index = 0; index < count; index++)
            {
                float delay = WaveDistance(index) * Mathf.Max(0f, enterCellStagger);
                maxDelay = Mathf.Max(maxDelay, delay);
                cells[index].PrepareForEnterAnimation();
                cells[index].PlayEnterAnimation(delay, enterCellDuration);
                if (index % 2 == 0)
                {
                    StartCoroutine(PlayEntryHapticRoutine(delay));
                }
            }

            yield return new WaitForSecondsRealtime(maxDelay + enterCellDuration);
            onComplete?.Invoke();
        }

        private IEnumerator PlayEntryHapticRoutine(float delay)
        {
            if (delay > 0f)
            {
                yield return new WaitForSecondsRealtime(delay);
            }

            Shared.Vibration.VibrationManager.Instance.PlayVibration(global::ImpactFeedbackStyle.Soft);
        }

        public void PlayLevelWinBoardAnimation(bool playParticles = true)
        {
            if (playParticles && leftWinConfetti != null)
            {
                leftWinConfetti.Play();
            }

            if (playParticles && rightWinConfetti != null)
            {
                rightWinConfetti.Play();
            }

            int count = Mathf.Min(CellCount, cells.Count);
            for (int index = 0; index < count; index++)
            {
                cells[index].PlayLevelWinPulse(WaveDistance(index) * Mathf.Max(0f, winPulseCellStagger), winPulseCellDuration, winPulseScaleMultiplier);
            }
        }

        public bool IsAnyLevelWinPulsePlaying()
        {
            for (int index = 0; index < cells.Count; index++)
            {
                if (cells[index].gameObject.activeInHierarchy && cells[index].IsPlayingLevelWinPulse)
                {
                    return true;
                }
            }

            return false;
        }

        public bool IsAnyCellAnimating()
        {
            for (int index = 0; index < cells.Count; index++)
            {
                if (cells[index].gameObject.activeInHierarchy && cells[index].IsAnimating)
                {
                    return true;
                }
            }

            return false;
        }

        // World-space X distance guaranteed to clear the viewport - the FTUE level-to-level slide.
        public float GetOffscreenSlideDistance()
        {
            Camera camera = InputCamera;
            if (camera != null && camera.orthographic)
            {
                return camera.orthographicSize * 2f * Mathf.Max(1f, camera.aspect);
            }

            return 20f;
        }

        public IEnumerator SlideLocalPosition(Vector3 targetLocalPosition, float durationSeconds)
        {
            Vector3 start = transform.localPosition;
            float duration = Mathf.Max(0.01f, durationSeconds);
            for (float elapsed = 0f; elapsed < duration; elapsed += Time.unscaledDeltaTime)
            {
                float t = Mathf.Clamp01(elapsed / duration);
                t = t < 0.5f ? 4f * t * t * t : 1f - Mathf.Pow(-2f * t + 2f, 3f) / 2f;
                transform.localPosition = Vector3.LerpUnclamped(start, targetLocalPosition, t);
                yield return null;
            }

            transform.localPosition = targetLocalPosition;
        }

        // ----- geometry queries -----

        public bool TryGetCellWorldPosition(int index, out Vector3 position)
        {
            if (index < 0 || index >= cells.Count || index >= CellCount)
            {
                position = default;
                return false;
            }

            position = cells[index].transform.position;
            return true;
        }

        public bool TryGetCellWorldBounds(int row, int column, out Bounds bounds)
        {
            return TryGetCellWorldBounds(row * size + column, out bounds);
        }

        public bool TryGetCellWorldBounds(int index, out Bounds bounds)
        {
            bounds = default;
            if (index < 0 || index >= CellCount || index >= cells.Count || cellContainer == null)
            {
                return false;
            }

            Vector3 center = cellContainer.TransformPoint(CellLocalCenter(index));
            float worldSize = spacing * cellFillPercent * Mathf.Abs(cellContainer.lossyScale.x);
            bounds = new Bounds(center, new Vector3(worldSize, worldSize, 0.1f));
            return true;
        }

        public bool TryGetCellsWorldBounds(IReadOnlyList<int> indices, out Bounds bounds)
        {
            bounds = default;
            bool hasBounds = false;
            if (indices == null)
            {
                return false;
            }

            for (int index = 0; index < indices.Count; index++)
            {
                if (!TryGetCellWorldBounds(indices[index], out Bounds cellBounds))
                {
                    continue;
                }

                if (!hasBounds)
                {
                    bounds = cellBounds;
                    hasBounds = true;
                }
                else
                {
                    bounds.Encapsulate(cellBounds);
                }
            }

            return hasBounds;
        }

        public bool TryGetBoardWorldBounds(out Bounds bounds)
        {
            bounds = default;
            if (CellCount <= 0 || !TryGetCellWorldBounds(0, out Bounds first) || !TryGetCellWorldBounds(CellCount - 1, out Bounds last))
            {
                return false;
            }

            bounds = first;
            bounds.Encapsulate(last);
            return true;
        }

        // ----- internals -----

        private void StepPointerTo(Vector2 screen)
        {
            if (!TryScreenToLocal(screen, out Vector2 local))
            {
                return;
            }

            // Walk from the previous pointer position in small steps so fast swipes don't skip cells.
            float distance = Vector2.Distance(lastPointerLocal, local);
            float step = Mathf.Max(0.0001f, spacing * 0.25f);
            int steps = Mathf.Max(1, Mathf.CeilToInt(distance / step));
            for (int index = 1; index <= steps; index++)
            {
                Vector2 sample = Vector2.Lerp(lastPointerLocal, local, (float)index / steps);
                int cell = LocalToCell(sample, 0f);
                if (cell >= 0 && cell != lastPointerCell)
                {
                    lastPointerCell = cell;
                    CellDragEntered?.Invoke(cell);
                }
            }

            lastPointerLocal = local;
        }

        private void ReleasePointer()
        {
            if (!pointerDown)
            {
                return;
            }

            pointerDown = false;
            pointerId = -1;
            lastPointerCell = -1;
            CellReleased?.Invoke();
        }

        private int ScreenToCell(Vector2 screen, float graceCells, out Vector2 local)
        {
            if (!TryScreenToLocal(screen, out local))
            {
                return -1;
            }

            return LocalToCell(local, graceCells);
        }

        private bool TryScreenToLocal(Vector2 screen, out Vector2 local)
        {
            local = default;
            Camera camera = InputCamera;
            if (camera == null || cellContainer == null || size <= 0)
            {
                return false;
            }

            Plane plane = new Plane(cellContainer.forward, cellContainer.position);
            Ray ray = camera.ScreenPointToRay(new Vector3(screen.x, screen.y, 0f));
            if (!plane.Raycast(ray, out float enter))
            {
                return false;
            }

            Vector3 localPoint = cellContainer.InverseTransformPoint(ray.GetPoint(enter));
            local = new Vector2(localPoint.x, localPoint.y);
            return true;
        }

        private int LocalToCell(Vector2 local, float graceCells)
        {
            if (size <= 0 || spacing <= 0f)
            {
                return -1;
            }

            float left = origin.x - spacing * 0.5f;
            float top = origin.y + spacing * 0.5f;
            float columnF = (local.x - left) / spacing;
            float rowF = (top - local.y) / spacing;
            if (columnF < -graceCells || rowF < -graceCells || columnF >= size + graceCells || rowF >= size + graceCells)
            {
                return -1;
            }

            int column = Mathf.Clamp(Mathf.FloorToInt(columnF), 0, size - 1);
            int row = Mathf.Clamp(Mathf.FloorToInt(rowF), 0, size - 1);
            return row * size + column;
        }

        private Vector3 CellLocalCenter(int index)
        {
            int row = index / Mathf.Max(1, size);
            int column = index % Mathf.Max(1, size);
            return new Vector3(origin.x + column * spacing, origin.y - row * spacing, -0.05f);
        }

        private float WaveDistance(int index)
        {
            return index / size + index % size;
        }

        private void AnimateGuidePath()
        {
            if (guidePath == null || !guidePath.enabled || guidePathCells == null)
            {
                return;
            }

            guidePathTime += Time.unscaledDeltaTime;
            Color color = guidePathColor;
            color.a = guidePathColor.a * (0.45f + 0.55f * (0.5f + 0.5f * Mathf.Sin(guidePathTime * 5f)));
            guidePath.startColor = color;
            guidePath.endColor = color;
        }

        private void FitBase()
        {
            Transform boardBase = baseTransform != null ? baseTransform : (transform.childCount > 0 ? transform.GetChild(0) : null);
            if (boardBase == null || basePadding <= 0f || boardBase == cellContainer)
            {
                return;
            }

            if (!baseOriginalScale.HasValue)
            {
                baseOriginalScale = boardBase.localScale;
                SpriteRenderer baseRenderer = boardBase.GetComponent<SpriteRenderer>();
                if (baseRenderer != null && baseRenderer.sprite != null)
                {
                    baseSpriteSize = baseRenderer.drawMode == SpriteDrawMode.Simple ? (Vector2)baseRenderer.sprite.bounds.size : baseRenderer.size;
                }
            }

            if (baseSpriteSize.x <= 0f || baseSpriteSize.y <= 0f)
            {
                return;
            }

            float target = boardWorldWidth + basePadding;
            boardBase.localPosition = new Vector3(boardBase.localPosition.x, boardCenterYOffset, boardBase.localPosition.z);
            boardBase.localScale = new Vector3(target / baseSpriteSize.x, target / baseSpriteSize.y, baseOriginalScale.Value.z);
        }

        private void EnsureCellCount(int required)
        {
            if (cellContainer == null || cellPrefab == null)
            {
                return;
            }

            while (cells.Count < required)
            {
                cells.Add(Instantiate(cellPrefab, cellContainer));
            }
        }

        private float GetBaseCellWorldSize()
        {
            return cellPrefab != null ? Mathf.Max(0.0001f, cellPrefab.TileSize) : 1f;
        }

        private LineRenderer EnsureLine(LineRenderer line, string lineName, int sortingOrder)
        {
            if (line != null)
            {
                return line;
            }

            var lineObject = new GameObject(lineName);
            lineObject.transform.SetParent(cellContainer != null ? cellContainer : transform, false);
            line = lineObject.AddComponent<LineRenderer>();
            line.useWorldSpace = false;
            line.numCapVertices = 6;
            line.numCornerVertices = 6;
            line.alignment = LineAlignment.TransformZ;
            line.sortingOrder = sortingOrder;
            Shader shader = Shader.Find("Sprites/Default");
            if (shader != null)
            {
                line.sharedMaterial = new Material(shader);
            }

            return line;
        }

        private static readonly List<RaycastResult> uiHitsScratch = new List<RaycastResult>();

        // True only when real canvas UI (a button, a popup) is under the pointer. Not
        // EventSystem.IsPointerOverGameObject: the camera also carries a Physics2DRaycaster, so
        // any world-space collider would count as "UI" and swallow presses meant for the board.
        private static bool IsOverUi(Vector2 screenPosition)
        {
            EventSystem eventSystem = EventSystem.current;
            if (eventSystem == null)
            {
                return false;
            }

            uiHitsScratch.Clear();
            eventSystem.RaycastAll(new PointerEventData(eventSystem) { position = screenPosition }, uiHitsScratch);
            for (int index = 0; index < uiHitsScratch.Count; index++)
            {
                if (uiHitsScratch[index].module is UnityEngine.UI.GraphicRaycaster)
                {
                    return true;
                }
            }

            return false;
        }

        private static bool TryGetPointerPressed(out Vector2 screenPosition, out int id)
        {
            if (Touchscreen.current != null)
            {
                foreach (TouchControl touch in Touchscreen.current.touches)
                {
                    if (touch.press.wasPressedThisFrame)
                    {
                        screenPosition = touch.position.ReadValue();
                        id = touch.touchId.ReadValue();
                        return true;
                    }
                }
            }

            if (Mouse.current != null && Mouse.current.leftButton.wasPressedThisFrame)
            {
                screenPosition = Mouse.current.position.ReadValue();
                id = -1;
                return true;
            }

            screenPosition = default;
            id = -1;
            return false;
        }

        private static bool TryGetPointerPosition(int id, out Vector2 screenPosition, out bool released)
        {
            if (id >= 0 && Touchscreen.current != null)
            {
                foreach (TouchControl touch in Touchscreen.current.touches)
                {
                    if (touch.touchId.ReadValue() != id)
                    {
                        continue;
                    }

                    screenPosition = touch.position.ReadValue();
                    released = touch.press.wasReleasedThisFrame || !touch.press.isPressed;
                    return true;
                }

                screenPosition = default;
                released = true;
                return false;
            }

            if (Mouse.current != null)
            {
                screenPosition = Mouse.current.position.ReadValue();
                released = Mouse.current.leftButton.wasReleasedThisFrame || !Mouse.current.leftButton.isPressed;
                return true;
            }

            screenPosition = default;
            released = true;
            return false;
        }
    }
}
