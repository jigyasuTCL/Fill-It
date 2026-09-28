using TMPro;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.UI;

namespace Shared.Notes
{
    public sealed class NotesManager : MonoBehaviour
    {
        [SerializeField] private Button notesButton;
        [SerializeField] private Image notesButtonImage;
        [SerializeField] private Button applyAllButton;
        [SerializeField] private Color inactiveColor = new Color(0.92f, 0.94f, 0.96f, 1f);
        [SerializeField] private Color activeColor = new Color(0.31f, 0.72f, 0.58f, 1f);
        [SerializeField] private bool notesActive;
        private bool applyAllAvailable;

        public NotesModeChangedEvent NotesModeChanged = new NotesModeChangedEvent();
        public UnityEvent ApplyAllRequested = new UnityEvent();

        public bool IsNotesActive => notesActive;

        private void Awake()
        {
            ResolveReferences();
            Refresh();
        }

        private void OnEnable()
        {
            ResolveReferences();
            if (notesButton != null)
            {
                notesButton.onClick.RemoveListener(ToggleNotes);
                notesButton.onClick.AddListener(ToggleNotes);
            }

            if (applyAllButton != null)
            {
                applyAllButton.onClick.RemoveListener(RequestApplyAll);
                applyAllButton.onClick.AddListener(RequestApplyAll);
            }

            Refresh();
        }

        private void OnDisable()
        {
            if (notesButton != null)
            {
                notesButton.onClick.RemoveListener(ToggleNotes);
            }

            if (applyAllButton != null)
            {
                applyAllButton.onClick.RemoveListener(RequestApplyAll);
            }
        }

        public void ToggleNotes()
        {
            SetNotesActive(!notesActive);
        }

        public void SetNotesActive(bool active)
        {
            if (notesActive == active)
            {
                Refresh();
                return;
            }

            notesActive = active;
            Refresh();
            NotesModeChanged.Invoke(notesActive);
        }

        public void RequestApplyAll()
        {
            ApplyAllRequested.Invoke();
        }

        /// <summary>
        /// Drives the Apply All button's interactable state (1+ tentative cells required). The
        /// gameplay layer recomputes and calls this after every action that can change the
        /// tentative count: placing/editing/clearing a note, committing one, Apply All, Undo,
        /// and level reset/reload.
        /// </summary>
        public void SetApplyAllAvailable(bool available)
        {
            applyAllAvailable = available;
            Refresh();
        }

        public void Refresh()
        {
            ResolveReferences();
            if (notesButtonImage != null)
            {
                notesButtonImage.color = notesActive ? activeColor : inactiveColor;
            }

            if (applyAllButton != null)
            {
                applyAllButton.gameObject.SetActive(notesActive);
                applyAllButton.interactable = applyAllAvailable;
            }
        }

        private void ResolveReferences()
        {
            if (notesButton == null)
            {
                notesButton = FindNamedComponent<Button>("NotesButton");
            }

            if (notesButtonImage == null && notesButton != null)
            {
                notesButtonImage = notesButton.GetComponent<Image>();
            }

            if (applyAllButton == null)
            {
                applyAllButton = FindNamedComponent<Button>("ApplyAllNotesButton");
            }

            if (applyAllButton == null && notesButton != null)
            {
                applyAllButton = CreateApplyAllButton(notesButton.transform.parent);
            }
        }

        private Button CreateApplyAllButton(Transform parent)
        {
            if (parent == null)
            {
                return null;
            }

            GameObject root = new GameObject("ApplyAllNotesButton", typeof(RectTransform), typeof(CanvasRenderer), typeof(Image), typeof(Button));
            root.transform.SetParent(parent, false);
            RectTransform rect = root.GetComponent<RectTransform>();
            rect.anchorMin = new Vector2(1f, 0.5f);
            rect.anchorMax = new Vector2(1f, 0.5f);
            rect.pivot = new Vector2(0.5f, 0.5f);
            rect.anchoredPosition = new Vector2(-25f, -82f);
            rect.sizeDelta = new Vector2(190f, 56f);

            Image image = root.GetComponent<Image>();
            image.color = activeColor;

            Button button = root.GetComponent<Button>();
            button.targetGraphic = image;

            GameObject labelRoot = new GameObject("Label", typeof(RectTransform), typeof(CanvasRenderer), typeof(TextMeshProUGUI));
            labelRoot.transform.SetParent(root.transform, false);
            RectTransform labelRect = labelRoot.GetComponent<RectTransform>();
            labelRect.anchorMin = Vector2.zero;
            labelRect.anchorMax = Vector2.one;
            labelRect.offsetMin = Vector2.zero;
            labelRect.offsetMax = Vector2.zero;

            TextMeshProUGUI label = labelRoot.GetComponent<TextMeshProUGUI>();
            label.text = "Apply All";
            label.fontSize = 24f;
            label.color = Color.white;
            label.alignment = TextAlignmentOptions.Center;

            return button;
        }

        private T FindNamedComponent<T>(string objectName) where T : Component
        {
            Transform searchRoot = transform.root != null ? transform.root : transform;
            T[] components = searchRoot.GetComponentsInChildren<T>(true);
            for (int index = 0; index < components.Length; index++)
            {
                if (components[index].name == objectName)
                {
                    return components[index];
                }
            }

            return null;
        }
    }

    [System.Serializable]
    public sealed class NotesModeChangedEvent : UnityEvent<bool>
    {
    }
}
