using System;
using System.Collections.Generic;
using System.Collections;
using System.Reflection;
using System.Text;
using UnityEngine;
using UnityEngine.UI;

namespace Torment.VerticalDialogue
{
    // Compiled against the game's own Mono/Unity assemblies, not a modern runtime.
    public static class Layout
    {
        private static readonly Dictionary<int, State> states = new Dictionary<int, State>();
        private static bool failed;
        private sealed class Reveal
        {
            public float height, remaining, time;
            public bool followBottom;
        }
        private static readonly Dictionary<int, Reveal> reveals = new Dictionary<int, Reveal>();
        private sealed class HiddenPortrait
        {
            public CanvasGroup group;
            public float alpha;
            public bool interactable, blocksRaycasts;
        }
        private static readonly Dictionary<int, HiddenPortrait> hiddenPortraits = new Dictionary<int, HiddenPortrait>();
        private static Component partyHud, playerHudOwner;
        private static Component playerPortraitHud;
        private static bool playerHudConversation;
        private static Component currentConversation;
        private static bool hudSyncFailed;
        private static int continueShortcutWindow;
        private static bool continueShortcutReady, continueShortcutUsed;

        public static void BeginContinueShortcutFrame(Component window)
        {
            continueShortcutWindow = window.GetInstanceID();
            continueShortcutReady = false;
            continueShortcutUsed = false;
            if (!(Input.GetKeyDown(KeyCode.Alpha1) || Input.GetKeyDown(KeyCode.Keypad1))) return;
            if (Input.GetKey(KeyCode.LeftShift) || Input.GetKey(KeyCode.RightShift) ||
                Input.GetKey(KeyCode.LeftControl) || Input.GetKey(KeyCode.RightControl) ||
                Input.GetKey(KeyCode.LeftAlt) || Input.GetKey(KeyCode.RightAlt)) return;
            const BindingFlags flags = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
            Type type = window.GetType();
            object printer = type.GetField("_nodePrinter", flags).GetValue(window);
            if (printer == null || (bool)printer.GetType().GetProperty("HasResponses", flags).GetValue(printer, null)) return;
            continueShortcutReady = (bool)type.GetProperty("AbleToContinue", flags).GetValue(window, null) ||
                (bool)type.GetField("_partialScrollMode", flags).GetValue(window);
        }

        public static bool ContinueShortcut(bool originalInput, Component window)
        {
            bool extra = continueShortcutReady && !continueShortcutUsed && window.GetInstanceID() == continueShortcutWindow;
            if (originalInput || extra) continueShortcutUsed = true;
            return originalInput || extra;
        }
        private sealed class PortraitHoverState
        {
            public Component button;
            public PropertyInfo transition;
            public object originalTransition;
            public readonly Dictionary<Behaviour, bool> graphics = new Dictionary<Behaviour, bool>();
        }
        private static readonly Dictionary<int, PortraitHoverState> portraitHoverStates = new Dictionary<int, PortraitHoverState>();

        private static void SuppressPortraitHover(Component portrait)
        {
            int id = portrait.GetInstanceID();
            PortraitHoverState state;
            if (!portraitHoverStates.TryGetValue(id, out state))
            {
                state = new PortraitHoverState();
                Component image = portrait.GetType().GetField("portraitImage").GetValue(portrait) as Component;
                if (image != null)
                {
                    foreach (Component component in image.GetComponents<Component>())
                        if (component != null && component.GetType().Name == "Button") { state.button = component; break; }
                    if (state.button != null)
                    {
                        state.transition = state.button.GetType().GetProperty("transition");
                        state.originalTransition = state.transition.GetValue(state.button, null);
                    }
                    Transform parent = image.transform.parent.parent;
                    foreach (string name in new string[] { "Selected", "Mouse Over", "Hover Sprite" })
                    {
                        Transform child = parent.Find(name);
                        if (child == null) continue;
                        foreach (Behaviour graphic in child.GetComponents<Behaviour>())
                            if (graphic.GetType().Name == "Image") state.graphics.Add(graphic, graphic.enabled);
                    }
                }
                portraitHoverStates.Add(id, state);
            }
            if (state.button != null && state.transition != null)
            {
                state.transition.SetValue(state.button, Enum.ToObject(state.transition.PropertyType, 0), null);
                Component graphic = state.button.GetType().GetProperty("targetGraphic").GetValue(state.button, null) as Component;
                if (graphic != null)
                {
                    CanvasRenderer renderer = graphic.GetComponent<CanvasRenderer>();
                    if (renderer != null) renderer.SetColor(Color.white);
                }
            }
            foreach (Behaviour graphic in state.graphics.Keys) if (graphic != null) graphic.enabled = false;
        }

        public static bool UseDialogueControl(int which)
        {
            if (!playerHudConversation || currentConversation == null) return false;
            currentConversation.GetType().GetMethod(which == 0 ? "UseItemButton" : "UseAbilityButton", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic, null, Type.EmptyTypes, null).Invoke(currentConversation, null);
            return true;
        }

        public static bool UseVisualDialogueScroll(Component wheel)
        {
            return wheel != null && wheel.name == "Conversation Text" &&
                wheel.transform.parent != null && wheel.transform.parent.name == "Text Panel";
        }

        public static bool DialogueHudActive() { return playerHudConversation; }

        public static bool HasSpeakerLabel(string text)
        {
            if (String.IsNullOrEmpty(text)) return false;
            int open = text.IndexOf("<color", StringComparison.Ordinal);
            if (open < 0) return false;
            int start = text.IndexOf('>', open);
            if (start < 0) return false;
            int end = text.IndexOf("</color>", start + 1, StringComparison.Ordinal);
            if (start < 0 || end <= start) return false;
            string label = text.Substring(start + 1, end - start - 1);
            if (!text.Substring(end + 8).TrimStart().StartsWith("<color", StringComparison.Ordinal)) return false;
            return label.IndexOf('<') < 0 && (label.EndsWith(" - ", StringComparison.Ordinal) || label.TrimEnd().EndsWith(":", StringComparison.Ordinal));
        }

        public static string ApplySpeechIndent(string entry)
        {
            if (!HasSpeakerLabel(entry)) return entry;
            int nameStart = entry.IndexOf('>', entry.IndexOf("<color", StringComparison.Ordinal)) + 1;
            int nameEnd = entry.IndexOf("</color>", nameStart, StringComparison.Ordinal);
            string label = entry.Substring(nameStart, nameEnd - nameStart).ToUpperInvariant();
            if (label.TrimEnd().EndsWith(":", StringComparison.Ordinal))
                label = label.TrimEnd().TrimEnd(':').TrimEnd() + " - ";
            entry = entry.Substring(0, nameStart) + label + entry.Substring(nameEnd);
            if (!entry.StartsWith("[padding=", StringComparison.Ordinal)) return entry;
            int end = entry.IndexOf(']');
            int comma = end < 0 ? -1 : entry.LastIndexOf(',', end);
            if (comma < 0) return entry;
            // Native padding order is top,right,bottom,left. Reserve the indent
            // before measuring, then the mesh modifier outdents only line one.
            return entry.Substring(0, comma + 1) + "32" + entry.Substring(end);
        }
        public static bool BlockPortraitAction(Component portrait)
        {
            return playerHudConversation && portrait != null && portraitHoverStates.ContainsKey(portrait.GetInstanceID());
        }

        private static Component AsComponent(object value)
        {
            Component component = value as Component;
            GameObject go = value as GameObject;
            return component != null ? component : go != null ? go.transform : null;
        }

        private static void HideExplorationChoices(Component row)
        {
            if (row == null) return;
            const BindingFlags flags = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
            Component container = AsComponent(row.GetType().GetField("activeButtonContainer", flags).GetValue(row));
            if (container == null) return;
            CanvasGroup group = container.GetComponent<CanvasGroup>();
            if (group == null) group = container.gameObject.AddComponent<CanvasGroup>();
            int id = container.GetInstanceID();
            if (!hiddenPortraits.ContainsKey(id))
                hiddenPortraits.Add(id, new HiddenPortrait { group = group, alpha = group.alpha, interactable = group.interactable, blocksRaycasts = group.blocksRaycasts });
            group.alpha = 0f; group.interactable = false; group.blocksRaycasts = false;
        }

        public static void SyncPlayerHud(Component party, Component player, bool conversation)
        {
            playerHudConversation = conversation;
            if (hudSyncFailed) return;
            try { SyncPlayerHudCore(party, player, conversation); }
            catch (Exception ex)
            {
                hudSyncFailed = true;
                Debug.LogError("[VerticalDialogue] Player HUD update stopped; dialogue layout retained: " + ex);
            }
        }

        private static void SyncPlayerHudCore(Component party, Component player, bool conversation)
        {
            partyHud = party;
            playerHudOwner = player;
            playerHudConversation = conversation;
            if (!conversation)
            {
                foreach (PortraitHoverState state in portraitHoverStates.Values)
                {
                    if (state.button != null && state.transition != null) state.transition.SetValue(state.button, state.originalTransition, null);
                    foreach (KeyValuePair<Behaviour, bool> graphic in state.graphics) if (graphic.Key != null) graphic.Key.enabled = graphic.Value;
                }
                portraitHoverStates.Clear();
                foreach (HiddenPortrait entry in hiddenPortraits.Values)
                    if (entry.group != null)
                    {
                        entry.group.alpha = entry.alpha;
                        entry.group.interactable = entry.interactable;
                        entry.group.blocksRaycasts = entry.blocksRaycasts;
                    }
                hiddenPortraits.Clear();
                return;
            }
            if (party == null || player == null) return;
            foreach (string field in new string[] { "ItemRow", "AbilityRow" })
                HideExplorationChoices(AsComponent(party.GetType().GetField(field).GetValue(party)));
            IList portraits = party.GetType().GetField("_items", BindingFlags.Instance | BindingFlags.NonPublic).GetValue(party) as IList;
            if (portraits == null) return;
            foreach (object value in portraits)
            {
                Component portrait = value as Component;
                if (portrait == null) continue;
                GameObject linked = portrait.GetType().GetField("LinkedObject").GetValue(portrait) as GameObject;
                if (linked == null) continue;
                if (linked == player.gameObject) playerPortraitHud = portrait;
                // Preserve native party layout and tooltip events; block actions for every member.
                SuppressPortraitHover(portrait);
                MethodInfo clearSelection = portrait.GetType().GetMethod("SetSelected", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic, null, new Type[] { typeof(bool) }, null);
                if (clearSelection == null) throw new MissingMethodException(portrait.GetType().FullName, "SetSelected(bool)");
                clearSelection.Invoke(portrait, new object[] { false });
            }
        }
        public static void RemoveResponsesFirst(object printer)
        {
            const BindingFlags flags = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
            Type type = printer.GetType();
            IList links = (IList)type.GetField("responseLinks", flags).GetValue(printer);
            object wheel = type.GetField("mainText", flags).GetValue(printer);
            Type wheelType = wheel.GetType();
            MethodInfo indexOf = wheelType.GetMethod("GetIndex", new Type[] { typeof(int) });
            MethodInfo getItem = wheelType.GetMethod("GetItem", new Type[] { typeof(int) });
            MethodInfo removeAt = wheelType.GetMethod("RemoveAt", new Type[] { typeof(int) });
            float height = 0f;
            List<int> indexes = new List<int>();
            foreach (object link in links)
            {
                int index = (int)indexOf.Invoke(wheel, new object[] { (int)link });
                if (index < 0) continue;
                object item = getItem.Invoke(wheel, new object[] { index });
                height += (float)item.GetType().GetProperty("CalculatedHeight").GetValue(item, null);
                indexes.Add(index);
            }
            type.GetField("_removedLinksHeight", flags).SetValue(printer, height);
            indexes.Sort();
            for (int i = indexes.Count - 1; i >= 0; i--)
                removeAt.Invoke(wheel, new object[] { indexes[i] });
            links.Clear();
            ((IList)type.GetField("responseNodeIDs", flags).GetValue(printer)).Clear();
            ((IList)type.GetField("responseStrings", flags).GetValue(printer)).Clear();
        }

        public static void Apply(Component window)
        {
            if (window == null || failed) return;
            try
            {
                int id = window.GetInstanceID();
                currentConversation = window;
                State state;
                if (!states.TryGetValue(id, out state))
                {
                    state = new State(window);
                    states.Add(id, state);
                }
                state.Update();
            }
            catch (Exception ex)
            {
                failed = true;
                Debug.LogError("[VerticalDialogue] Layout stopped after error: " + ex);
            }
        }

        public static Vector3 CameraOffset()
        {
            Camera camera = Camera.main;
            if (camera == null) return Vector3.zero;
            float occupied = Mathf.Clamp((Mathf.Min(Screen.height * 0.5358f, Screen.width * 0.3876f) + 76f) / Mathf.Max(1f, Screen.width), 0.20f, 0.43f);
            Vector3 center = camera.ViewportToWorldPoint(new Vector3(0.5f, 0.5f, camera.nearClipPlane));
            Vector3 shifted = camera.ViewportToWorldPoint(new Vector3(0.5f + occupied * 0.5f, 0.5f, camera.nearClipPlane));
            return shifted - center;
        }

        // Keep chronological top-to-bottom ordering and native scroll coordinates.
        // Only short conversation content needs a visual offset toward the bottom.
        public static float BottomPaintOffset(Component wheel, float position, float contentHeight, float viewHeight)
        {
            if (wheel != null && wheel.name == "Conversation Text" &&
                wheel.transform.parent != null && wheel.transform.parent.name == "Text Panel")
            {
                Reveal reveal;
                int id = wheel.GetInstanceID();
                if (!reveals.TryGetValue(id, out reveal))
                {
                    reveal = new Reveal();
                    reveal.time = Time.realtimeSinceStartup;
                    reveals.Add(id, reveal);
                }
                float now = Time.realtimeSinceStartup;
                reveal.remaining *= Mathf.Exp(-10f * Mathf.Max(0f, now - reveal.time));
                reveal.time = now;
                float growth = contentHeight - reveal.height;
                if (growth < -0.5f) { reveal.remaining = 0f; }
                else if (growth > 0.5f)
                {
                    reveal.remaining = Mathf.Min(viewHeight, reveal.remaining + growth);
                    reveal.followBottom = true;
                }
                reveal.height = contentHeight;
                if (ManualScrollInput()) { reveal.remaining = 0f; reveal.followBottom = false; }
                if (reveal.remaining < 0.5f) reveal.remaining = 0f;
                // Start new material below the bottom mask, then slide the complete
                // chronological stack upward. This also animates short conversations
                // whose ordinary scrollbar has no scrollable range.
                if (reveal.followBottom)
                    return contentHeight - viewHeight - reveal.remaining;
                return contentHeight < viewHeight ? contentHeight - viewHeight : position;
            }
            return position;
        }

        private static bool ManualScrollInput()
        {
            return Mathf.Abs(Input.GetAxisRaw("Mouse ScrollWheel")) > 0.001f ||
                Input.GetKey(KeyCode.PageUp) || Input.GetKey(KeyCode.PageDown) ||
                (Input.GetMouseButton(0) && Input.mousePosition.x > Screen.width - 100f);
        }

        public static string ModDialogueColours(string text)
        {
            if (String.IsNullOrEmpty(text)) return text;
            bool again = text.Contains("again");
            if (text.Contains("again?") ||
                (again && (text.Contains("Tell ") || text.Contains("tell") || text.Contains("Explain ") || text.Contains("explain") || text.Contains("talk about"))) ||
                text.Contains("Remind me") || text.Contains("more questions about") ||
                text.Contains("You're sure you ") || text.Contains("Are you sure you ") ||
                text.Contains("any more about ") || (text.Contains("talk about") && text.Contains("some more")))
                text = "<color=#747474>" + text + "</color>";
            return ModSkillColours(text).Replace("[Attack]", "<color=#dd0000>[Attack]</color>");
        }

        public static string ModSkillColours(string text)
        {
            if (String.IsNullOrEmpty(text)) return text;
            string[] names = { "Perception", "Anamnesis", "Persuasion", "Quick Fingers", "Concentration", "Cypher Use", "Endurance", "Lore: Machinery", "Lore: Natural", "Lore: Mystical", "Smashing", "Initiative", "Running", "Healing", "Stealth", "Deception", "Intimidation", "Tidal Affinity" };
            foreach (string name in names)
            {
                string label = "[" + name + "]";
                string coloured = "<color=#21cb03>" + label + "</color>";
                if (!text.Contains(coloured)) text = text.Replace(label, coloured);
            }
            return text;
        }

        public static void ConfigureItemCard(Component display)
        {
            const BindingFlags flags = BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public;
            Type type = display.GetType();
            // The content is counter-mirrored to keep labels readable, so positive
            // local X is screen-right. Enter from behind the dialogue column.
            type.GetField("deployedXPosition", flags).SetValue(display, 689f);
            type.GetField("hiddenXPosition", flags).SetValue(display, 1400f);
            type.GetField("deployedPosition", flags).SetValue(display, new Vector3(689f, 100f, 0f));
            type.GetField("hiddenPosition", flags).SetValue(display, new Vector3(1400f, 100f, 0f));
            RectTransform content = display.transform.Find("Content Container") as RectTransform;
            if (content == null) return;
            content.anchoredPosition = new Vector2(1400f, 100f);
            content.localScale = new Vector3(-1f, 1f, 1f);
            RectTransform background = content.Find("Background Sprite") as RectTransform;
            if (background != null)
            {
                background.anchoredPosition = Vector2.zero;
                background.localScale = Vector3.one;
            }
            // The card frame faces left, but its contents keep normal orientation.
            foreach (string path in new string[] { "Top Bar/Item Gained Label", "Item Name Bar/Item Name Label", "Item Icon", "Dismiss Button", "Dismiss Key (Console Only)", "Show Info Key (Console Only)" })
            {
                RectTransform rect = content.Find(path) as RectTransform;
                if (rect == null) continue;
                rect.anchoredPosition += new Vector2((1f - 2f * rect.pivot.x) * rect.rect.width, 0f);
                rect.localScale = new Vector3(-1f, 1f, 1f);
            }
            Debug.Log("[VerticalDialogue] Item card configured: mirrored frame, right-to-left entrance behind dialogue.");
            foreach (string name in new string[] { "Top Bar", "Item Icon", "Item Name Bar", "Dismiss Button" })
            {
                RectTransform rect = content.Find(name) as RectTransform;
                if (rect != null)
                    rect.anchoredPosition = new Vector2(331f - (0.5f - rect.pivot.x) * rect.rect.width, rect.anchoredPosition.y);
            }
            foreach (string barName in new string[] { "Top Bar", "Item Name Bar" })
            {
                RectTransform header = content.Find(barName) as RectTransform;
                if (header == null) continue;
                header.sizeDelta = new Vector2(barName == "Top Bar" ? 550f : 618f, header.sizeDelta.y);
                header.anchoredPosition = new Vector2(0f, header.anchoredPosition.y);
                RectTransform label = header.Find(barName == "Top Bar" ? "Item Gained Label" : "Item Name Label") as RectTransform;
                if (label != null)
                {
                    label.anchorMin = Vector2.zero;
                    label.anchorMax = Vector2.one;
                    label.pivot = new Vector2(0.5f, 0.5f);
                    label.sizeDelta = Vector2.zero;
                    label.anchoredPosition = Vector2.zero;
                    label.localScale = new Vector3(-1f, 1f, 1f);
                }
            }
            RectTransform icon = content.Find("Item Icon") as RectTransform;
            if (icon != null)
            {
                RectTransform frame = icon.Find("Frame Sprite") as RectTransform;
                if (frame != null)
                {
                    frame.anchorMin = Vector2.zero;
                    frame.anchorMax = Vector2.one;
                    frame.pivot = new Vector2(0.5f, 0.5f);
                    frame.anchoredPosition = Vector2.zero;
                    frame.sizeDelta = new Vector2(8f, 8f);
                }
                RectTransform glow = icon.Find("Highlight Sprite") as RectTransform;
                if (glow != null)
                {
                    glow.anchorMin = glow.anchorMax = new Vector2(0.5f, 0.5f);
                    glow.pivot = new Vector2(0.5f, 0.5f);
                    // The glow artwork's visible inset is asymmetric; align that
                    // inset with the icon rather than centering its padded texture.
                    glow.anchoredPosition = new Vector2(-12f, 3f);
                }
            }
        }

        private sealed class State
        {
            private readonly Component window;
            private readonly RectTransform root, middle, textPanel, mainText, left, right, lists;
            private readonly RectTransform top, bottom, leftBorder, rightBorder, handle, continueContainer;
            private readonly RectTransform screenEdgeTrim;
            private readonly RectTransform doorClip;
            private readonly RectTransform itemChoices, abilityChoices;
            private readonly RectTransform tideEffect, moneyCounter;
            private readonly List<Sprite> frameSprites = new List<Sprite>();
            private sealed class FrameGlow
            {
                public RectTransform mask, image;
                public Vector2 maskPosition, maskSize, imagePosition, imageSize;
                public bool top;
                public Image graphic;
                public float direction;
            }
            private readonly List<FrameGlow> frameGlows = new List<FrameGlow>();
            private readonly RectTransform topGlows;
            private readonly CanvasGroup obsoletePortraitPanel;
            private readonly List<Behaviour> obsoleteItemFrame = new List<Behaviour>();
            private readonly Component wheel, heightHandle;
            private readonly MethodInfo onSizeChange, repaint;
            private readonly FieldInfo targetHeight, animating;
            private readonly Behaviour leftDoorGraphic, rightDoorGraphic;
            private readonly MethodInfo setScrollValue;
            private readonly List<RectTransform> readableItemChildren = new List<RectTransform>();
            private readonly List<float> itemChildX = new List<float>();
            private readonly List<float> itemChildY = new List<float>();
            private Vector2 lastTextSize;
            private int frameCount;
            private bool logged;

            public State(Component target)
            {
                window = target;
                root = target.GetComponent<RectTransform>();
                middle = Required("MiddlePanel");
                textPanel = Required("MiddlePanel/Text Panel");
                mainText = Required("MiddlePanel/Text Panel/Conversation Text");
                left = Required("Left Panel");
                right = Required("Right Panel");
                tideEffect = Required("Left Panel/Speaker Panel/Content Container/LCO Potrait/Frame/PFX_TidalShifts");
                moneyCounter = Required("Left Panel/Speaker Panel/Content Container/Shin Container");
                // Preserve native controllers and currency bindings, moving only
                // the display transforms into the conversation's root canvas.
                tideEffect.SetParent(root, false);
                moneyCounter.SetParent(root, false);
                CanvasGroup moneyVisibility = moneyCounter.GetComponent<CanvasGroup>();
                if (moneyVisibility == null) moneyVisibility = moneyCounter.gameObject.AddComponent<CanvasGroup>();
                moneyVisibility.alpha = 0f;
                moneyVisibility.interactable = false;
                moneyVisibility.blocksRaycasts = false;
                obsoletePortraitPanel = left.GetComponent<CanvasGroup>();
                if (obsoletePortraitPanel == null) obsoletePortraitPanel = left.gameObject.AddComponent<CanvasGroup>();
                Transform notification = right.Find("Item Gained Display");
                foreach (Behaviour graphic in right.GetComponentsInChildren<Behaviour>(true))
                    if (graphic != null && graphic.GetType().Name == "Image" &&
                        (notification == null || !graphic.transform.IsChildOf(notification)))
                        obsoleteItemFrame.Add(graphic);
                lists = Required("List Container Panel");
                itemChoices = lists.Find("Item List Container") as RectTransform;
                abilityChoices = lists.Find("Ability List Container") as RectTransform;
                top = Required("MiddlePanel/Top Border");
                bottom = Required("MiddlePanel/Bottom Border");
                leftBorder = Required("MiddlePanel/Left Border");
                rightBorder = Required("MiddlePanel/Right Border");
                UseFixedFrameCorners(top);
                UseFixedFrameCorners(bottom);
                topGlows = top.Find("CornerGlows") as RectTransform;
                if (topGlows != null)
                {
                    topGlows.gameObject.SetActive(true);
                    foreach (RectTransform mask in topGlows) CaptureFrameGlow(mask, true);
                }
                foreach (RectTransform mask in bottom)
                    if (mask.name.StartsWith("Mask", StringComparison.Ordinal)) CaptureFrameGlow(mask, false);
                handle = Required("MiddlePanel/Handle");
                continueContainer = Required("MiddlePanel/Continue Button Container");
                wheel = NamedComponent(mainText, "HyperStringWheel");
                heightHandle = NamedComponent(handle, "ConversationWindowHandle");
                const BindingFlags doorFlags = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
                leftDoorGraphic = window.GetType().GetField("leftDoor", doorFlags).GetValue(window) as Behaviour;
                rightDoorGraphic = window.GetType().GetField("rightDoor", doorFlags).GetValue(window) as Behaviour;
                GameObject clipObject = new GameObject("Continue Cover Clip", typeof(RectTransform));
                doorClip = clipObject.GetComponent<RectTransform>();
                doorClip.SetParent(continueContainer, false);
                Set(doorClip, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(1200f, 400f));
                clipObject.AddComponent(Type.GetType("UnityEngine.UI.RectMask2D, UnityEngine.UI", true));
                if (leftDoorGraphic != null) leftDoorGraphic.transform.SetParent(doorClip, false);
                if (rightDoorGraphic != null) rightDoorGraphic.transform.SetParent(doorClip, false);
                right.SetAsFirstSibling();
                onSizeChange = wheel.GetType().GetMethod("OnSizeChange", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic, null, Type.EmptyTypes, null);
                repaint = wheel.GetType().GetMethod("Repaint", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic, null, Type.EmptyTypes, null);
                targetHeight = heightHandle.GetType().GetField("_targetHeight", BindingFlags.Instance | BindingFlags.NonPublic);
                animating = heightHandle.GetType().GetField("_animating", BindingFlags.Instance | BindingFlags.NonPublic);
                setScrollValue = wheel.GetType().GetMethod("SetScrollValue", new Type[] { typeof(float) });
                foreach (string childName in new string[] { "Content Panel", "Console Keybind Display", "Item Gained Display" })
                {
                    RectTransform child = right.Find(childName) as RectTransform;
                    if (child != null)
                    {
                        readableItemChildren.Add(child);
                        itemChildX.Add(child.anchoredPosition.x);
                        itemChildY.Add(790f * (1f - 2f * child.anchorMin.y) - child.anchoredPosition.y);
                    }
                }
                // The root Animator stays enabled: it owns DelayedShow/DelayedHide callbacks.
                // Only the old bottom-edge height-drag handler is disabled.
                Behaviour oldDrag = heightHandle as Behaviour;
                if (oldDrag != null) oldDrag.enabled = false;
                screenEdgeTrim = root.Find("Screen Safe Space Offset") as RectTransform;
                if (screenEdgeTrim != null)
                {
                    screenEdgeTrim.gameObject.SetActive(true);
                    screenEdgeTrim.SetAsFirstSibling();
                    foreach (Image graphic in screenEdgeTrim.GetComponentsInChildren<Image>(true)) graphic.raycastTarget = false;
                }
                Disable("MiddlePanel/Top Border/FX");
                Disable("Left Panel/Speaker Panel/Content Container/FX_Ambient");
                Disable("Left Panel/Speaker Panel/Content Container/S_UI_LeftContentPanel_trail_01");
                // Keep native item/ability controls with the portrait instead of at their old screen position.
                lists.SetParent(root, false);
                Set(lists, Vector2.zero, Vector2.one, new Vector2(0.5f, 0.5f), Vector2.zero, Vector2.zero);
                CanvasGroup oldControls = Required("Left Panel/Ability Panel").GetComponent<CanvasGroup>();
                if (oldControls == null) oldControls = Required("Left Panel/Ability Panel").gameObject.AddComponent<CanvasGroup>();
                oldControls.alpha = 0f; oldControls.interactable = false; oldControls.blocksRaycasts = false;
                Debug.Log("[VerticalDialogue] v0.1 attached; native dialogue, Animator, item queue and input retained.");
                foreach (Text text in mainText.GetComponentsInChildren<Text>(true))
                    if (text.GetType().Name == "HyperText" && text.GetComponent<HangingIndent>() == null)
                        text.gameObject.AddComponent<HangingIndent>();
            }

            private RectTransform Required(string path)
            {
                Transform child = root.Find(path);
                if (child == null) throw new InvalidOperationException("Missing UI path: " + path);
                RectTransform rect = child.GetComponent<RectTransform>();
                if (rect == null) throw new InvalidOperationException("Missing RectTransform: " + path);
                return rect;
            }

            private void Disable(string path)
            {
                Transform t = root.Find(path);
                if (t != null) t.gameObject.SetActive(false);
            }

            public void Update()
            {
                if (partyHud != null && playerHudConversation) SyncPlayerHud(partyHud, playerHudOwner, true);
                // Restore native covers at their original aspect ratio; crop only
                // the outer overhang, leaving the circular center undistorted.
                if (leftDoorGraphic != null) leftDoorGraphic.enabled = true;
                if (rightDoorGraphic != null) rightDoorGraphic.enabled = true;
                RectTransform parent = root.parent as RectTransform;
                if (parent == null || parent.rect.height < 100f) return;
                // Enforce after animation, retaining its show/hide events and native active state.
                Set(root, Vector2.zero, Vector2.one, new Vector2(0.5f, 0.5f), Vector2.zero, Vector2.zero);
                SetScale(root, Vector3.one);
                float canvasHeight = parent.rect.height;
                float canvasWidth = parent.rect.width;
                float scale = canvasHeight / 2160f;
                float panelWidth = Mathf.Min(canvasHeight * 0.5358f, canvasWidth * 0.3876f);
                float panelHeight = canvasHeight - 120f * scale;
                float edge = 100f * scale;
                if (screenEdgeTrim != null)
                {
                    // Rotate the original sliced bottom trim, preserving its end
                    // caps and thickness, with the raised edge facing inward.
                    Set(screenEdgeTrim, new Vector2(1f, 0.5f), new Vector2(1f, 0.5f), new Vector2(0.5f, 0f), Vector2.zero,
                        new Vector2(canvasHeight, 217f * scale));
                    screenEdgeTrim.localRotation = Quaternion.Euler(0f, 0f, 90f);
                    SetScale(screenEdgeTrim, Vector3.one);
                }
                Set(middle, Vector2.one, Vector2.one, Vector2.one,
                    new Vector2(-edge, -60f * scale), new Vector2(panelWidth, panelHeight));
                SetScale(middle, Vector3.one);

                Set(textPanel, Vector2.zero, Vector2.one, new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(0f, -80f * scale));
                // Preserve the native text mask, margins, scrollbar, generated entry panels and controls.
                Set(mainText, Vector2.zero, Vector2.one, Vector2.zero,
                    new Vector2(60f * scale, 110f * scale), new Vector2(-181f * scale, -174f * scale));

                Set(top, new Vector2(0.5f, 1f), new Vector2(0.5f, 1f), new Vector2(0.5f, 1f), Vector2.zero,
                    new Vector2(panelWidth + 105f * scale, 171f * scale));
                Set(bottom, new Vector2(0.5f, 0f), new Vector2(0.5f, 0f), new Vector2(0.5f, 0f), Vector2.zero,
                    new Vector2(panelWidth + 105f * scale, 178f * scale));
                Set(leftBorder, new Vector2(0f, 0f), new Vector2(0f, 1f), new Vector2(0f, 1f),
                    new Vector2(-52.5f * scale, -171f * scale), new Vector2(91f * scale, -349f * scale));
                Set(rightBorder, new Vector2(1f, 0f), Vector2.one, Vector2.one,
                    new Vector2(52.5f * scale, -171f * scale), new Vector2(91f * scale, -349f * scale));
                UpdateFrameGlows(panelWidth + 105f * scale, panelHeight, scale);
                Set(handle, new Vector2(0f, 1f), Vector2.one, new Vector2(0.5f, 1f), Vector2.zero, new Vector2(0f, 76f * scale));

                Set(continueContainer, new Vector2(0.5f, 0f), new Vector2(0.5f, 0f), new Vector2(0.5f, 0f), new Vector2(0f, 12f * scale), new Vector2(436f, 70f));
                float buttonScale = Mathf.Min(0.8f * scale, panelWidth / 1250f);
                buttonScale = Mathf.Min(buttonScale, (panelWidth + 105f * scale) / 1388f);
                SetScale(continueContainer, new Vector3(buttonScale, buttonScale, 1f));
                Set(doorClip, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), Vector2.zero,
                    new Vector2(Mathf.Max(panelWidth - 480f * scale, 660f * buttonScale) / buttonScale, 400f));
                if (leftDoorGraphic != null)
                {
                    RectTransform door = leftDoorGraphic.GetComponent<RectTransform>();
                    door.sizeDelta = new Vector2(534f, 129f);
                }
                if (rightDoorGraphic != null)
                {
                    RectTransform door = rightDoorGraphic.GetComponent<RectTransform>();
                    door.sizeDelta = new Vector2(534f, 129f);
                }

                float sideScale = 0.72f * scale;
                float sideRight = edge + panelWidth + 38f * scale;
                float sideTop = 260f * scale;
                Set(left, Vector2.one, Vector2.one, Vector2.one,
                    new Vector2(-sideRight, -sideTop), new Vector2(780f, 790f));
                SetScale(left, new Vector3(sideScale, sideScale, 1f));
                float itemScale = sideScale * 780f / 714f;
                float rewardBottom = 92f * scale + 125f * buttonScale;
                // The native item's container starts 19 units above the side
                // panel's bottom, with its content raised by another 100 units.
                float rewardOffset = (790f - 19f - 100f) * itemScale;
                Set(right, Vector2.one, Vector2.one, Vector2.zero,
                    new Vector2(-sideRight - 12f * scale, -canvasHeight + rewardBottom + rewardOffset), new Vector2(714f, 790f));
                SetScale(right, new Vector3(-itemScale, -itemScale, 1f));
                // Mirror the frame toward the dialogue column; counter-mirror content
                // without changing the item queue's cached local animation positions.
                for (int i = 0; i < readableItemChildren.Count; i++)
                {
                    RectTransform child = readableItemChildren[i];
                    child.anchoredPosition = new Vector2(-itemChildX[i], itemChildY[i]);
                    SetScale(child, new Vector3(-1f, -1f, 1f));
                }

                // Stop persisted bottom-panel drag state from resizing the new column.
                if (targetHeight != null) targetHeight.SetValue(heightHandle, 1000f);
                if (animating != null) animating.SetValue(heightHandle, false);

                PlaceDialogueChoices("ItemRow", itemChoices);
                PlaceDialogueChoices("AbilityRow", abilityChoices);
                PlacePlayerFeedback();
                // Keep the old objects/controllers alive for native references;
                // suppress only the duplicate side-panel presentation.
                obsoletePortraitPanel.alpha = 0f;
                obsoletePortraitPanel.interactable = false;
                obsoletePortraitPanel.blocksRaycasts = false;
                foreach (Behaviour graphic in obsoleteItemFrame) if (graphic != null) graphic.enabled = false;
                frameCount++;
                Vector2 size = new Vector2(mainText.rect.width, mainText.rect.height);
                if (Mathf.Abs(size.x - lastTextSize.x) > 0.5f || Mathf.Abs(size.y - lastTextSize.y) > 0.5f)
                {
                    lastTextSize = size;
                    if (onSizeChange != null) onSizeChange.Invoke(wheel, null);
                    if (repaint != null) repaint.Invoke(wheel, null);
                }
                // One owner controls visual movement. Keep the native scroll value
                // at that same endpoint while its selection animators finish, so
                // handing control back cannot snap to a different scroll position.
                Reveal activeReveal;
                if (reveals.TryGetValue(wheel.GetInstanceID(), out activeReveal) &&
                    activeReveal.followBottom)
                {
                    if (ManualScrollInput()) { activeReveal.remaining = 0f; activeReveal.followBottom = false; }
                    else setScrollValue.Invoke(wheel, new object[] { 1f });
                    repaint.Invoke(wheel, null);
                }
                if (!logged && frameCount > 5)
                {
                    logged = true;
                    Debug.Log("[VerticalDialogue] referenceCanvas=" + canvasWidth + "x" + canvasHeight +
                        " column=" + middle.rect.width + "x" + middle.rect.height +
                        " text=" + mainText.rect.width + "x" + mainText.rect.height +
                        " screen=" + Screen.width + "x" + Screen.height);
                }
            }

            private void PlaceDialogueChoices(string field, RectTransform choices)
            {
                if (partyHud == null || choices == null) return;
                Component row = AsComponent(partyHud.GetType().GetField(field).GetValue(partyHud));
                if (row == null) return;
                Component button = AsComponent(row.GetType().GetField("toggleButton", BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public).GetValue(row));
                RectTransform buttonRect = button == null ? null : button.GetComponent<RectTransform>();
                if (buttonRect == null) return;
                Component nativeButton = window.GetType().GetField(field == "ItemRow" ? "ItemButton" : "AbilityButton", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic).GetValue(window) as Component;
                PropertyInfo enabledProperty = button.GetType().GetProperty("interactable");
                if (nativeButton != null && enabledProperty != null)
                    enabledProperty.SetValue(button, nativeButton.GetType().GetProperty("interactable").GetValue(nativeButton, null), null);
                Vector3 point = root.InverseTransformPoint(buttonRect.TransformPoint(new Vector3(buttonRect.rect.xMin, buttonRect.rect.center.y, 0f)));
                Vector3 buttonRight = root.InverseTransformPoint(buttonRect.TransformPoint(new Vector3(buttonRect.rect.xMax, buttonRect.rect.center.y, 0f)));
                float iconWidth = Mathf.Max(100f, Mathf.Abs(buttonRight.x - point.x));
                float iconHeight = iconWidth;
                DisableRowLayout(choices);
                RectTransform fake = choices.Find(field == "ItemRow" ? "Fake Item Button" : "Fake Ability Button") as RectTransform;
                RectTransform stack = choices.Find(field == "ItemRow" ? "Items Stack" : "Abilities Stack") as RectTransform;
                float width = 0f;
                if (fake != null)
                {
                    // Retain the native object for tooltip/controller references,
                    // but do not draw a second, slightly different toggle icon.
                    CanvasGroup fakeGroup = fake.GetComponent<CanvasGroup>();
                    if (fakeGroup == null) fakeGroup = fake.gameObject.AddComponent<CanvasGroup>();
                    fakeGroup.alpha = 0f;
                    fakeGroup.interactable = false;
                    fakeGroup.blocksRaycasts = false;
                }
                if (stack != null)
                {
                    DisableRowLayout(stack);
                    float x = 0f;
                    for (int i = 0; i < stack.childCount; i++)
                    {
                        RectTransform entry = stack.GetChild(i) as RectTransform;
                        if (entry == null || !entry.gameObject.activeSelf) continue;
                        Set(entry, new Vector2(0f, 0.5f), new Vector2(0f, 0.5f), new Vector2(0f, 0.5f), new Vector2(x, 0f), new Vector2(iconWidth, iconHeight));
                        SetScale(entry, Vector3.one);
                        x += iconWidth + 8f;
                    }
                    Set(stack, new Vector2(0f, 0.5f), new Vector2(0f, 0.5f), new Vector2(0f, 0.5f), Vector2.zero, new Vector2(x, iconHeight));
                    SetScale(stack, Vector3.one);
                    width = Mathf.Max(0f, x - 8f);
                }
                Set(choices, Vector2.zero, Vector2.zero, new Vector2(0f, 0.5f), new Vector2(Mathf.Max(0f, buttonRight.x - root.rect.xMin) + 8f, point.y - root.rect.yMin), new Vector2(width, iconHeight));
                SetScale(choices, Vector3.one);
            }

            private void PlacePlayerFeedback()
            {
                if (playerPortraitHud == null) return;
                Component image = playerPortraitHud.GetType().GetField("portraitImage").GetValue(playerPortraitHud) as Component;
                RectTransform portrait = image == null ? null : image.GetComponent<RectTransform>();
                if (portrait == null) return;
                Vector3 center = root.InverseTransformPoint(portrait.TransformPoint(portrait.rect.center));
                Vector3 leftEdge = root.InverseTransformPoint(portrait.TransformPoint(new Vector3(portrait.rect.xMin, portrait.rect.center.y, 0f)));
                Vector3 rightEdge = root.InverseTransformPoint(portrait.TransformPoint(new Vector3(portrait.rect.xMax, portrait.rect.center.y, 0f)));
                float ratio = Mathf.Abs(rightEdge.x - leftEdge.x) / 232f;
                Vector2 origin = new Vector2(center.x - root.rect.xMin, center.y - root.rect.yMin);
                Set(tideEffect, Vector2.zero, Vector2.zero, new Vector2(0.5f, 0.5f), origin, new Vector2(100f, 100f));
                SetScale(tideEffect, new Vector3(ratio, ratio, ratio));
                Set(moneyCounter, Vector2.zero, Vector2.zero, new Vector2(0f, 0.5f),
                    new Vector2(rightEdge.x - root.rect.xMin + 370f * ratio, origin.y - 4f * ratio), new Vector2(318f, 96f));
                float counterScale = ratio * 0.72f;
                SetScale(moneyCounter, new Vector3(counterScale, counterScale, 1f));
            }

            private static void DisableRowLayout(RectTransform rect)
            {
                foreach (Behaviour behaviour in rect.GetComponents<Behaviour>())
                    if (behaviour != null && (behaviour.GetType().Name == "HorizontalLayoutGroup" || behaviour.GetType().Name == "ContentSizeFitter"))
                        behaviour.enabled = false;
            }

            private void CaptureFrameGlow(RectTransform mask, bool isTop)
            {
                if (mask.childCount == 0) return;
                RectTransform image = mask.GetChild(0) as RectTransform;
                if (image == null) return;
                Image graphic = image.GetComponent<Image>();
                if (graphic == null) return;
                frameGlows.Add(new FrameGlow { mask = mask, image = image, graphic = graphic,
                    maskPosition = mask.anchoredPosition, maskSize = mask.sizeDelta,
                    imagePosition = image.anchoredPosition, imageSize = image.sizeDelta,
                    direction = mask.localScale.x < 0f ? -1f : 1f, top = isTop });
            }

            private static float GlowX(float source, float width, float scale)
            {
                float half = 1065.5f * scale, corner = 240f * scale;
                float distance = Mathf.Abs(source);
                if (distance >= half - corner) return Mathf.Sign(source) * (width * 0.5f - (half - distance));
                return source * (width - corner * 2f) / (half * 2f - corner * 2f);
            }

            private void UpdateFrameGlows(float width, float height, float scale)
            {
                if (topGlows != null)
                    Set(topGlows, new Vector2(0.5f, 1f), new Vector2(0.5f, 1f), new Vector2(0.5f, 0.5f), new Vector2(0f, -height * 0.5f), new Vector2(100f, 100f));
                for (int i = 0; i < frameGlows.Count; i++)
                {
                    FrameGlow glow = frameGlows[i];
                    float x = GlowX(glow.maskPosition.x * scale, width, scale);
                    float other = GlowX((glow.maskPosition.x - glow.direction * glow.maskSize.x) * scale, width, scale);
                    float glowWidth = Mathf.Abs(x - other);
                    float ratio = glowWidth / glow.maskSize.x;
                    float y = glow.maskPosition.y * scale + (glow.top ? (height - 840f * scale) * 0.5f : 0f);
                    Set(glow.mask, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(1f, 0.5f), new Vector2(x, y), new Vector2(glowWidth, glow.maskSize.y * scale));
                    glow.image.anchoredPosition = new Vector2(glow.imagePosition.x * ratio, glow.imagePosition.y * scale);
                    glow.image.sizeDelta = new Vector2(glow.imageSize.x * ratio, glow.imageSize.y * scale);
                    // Original Animator bindings still own alpha/fillAmount.
                    // Only geometry is adapted to the vertical frame.
                }
            }

            private void UseFixedFrameCorners(RectTransform rect)
            {
                Component image = NamedComponent(rect, "Image");
                PropertyInfo spriteProperty = image.GetType().GetProperty("sprite");
                Sprite source = spriteProperty.GetValue(image, null) as Sprite;
                if (source == null) throw new InvalidOperationException("Missing frame sprite on " + rect.name);
                // The atlas uses tight meshes, so textureRect is not reliable for
                // these sprites. The UV bounds locate their unrotated atlas area;
                // the original rect gives the untrimmed pixel dimensions.
                Vector2[] uv = source.uv;
                float minX = 1f, minY = 1f;
                for (int i = 0; i < uv.Length; i++)
                {
                    minX = Mathf.Min(minX, uv[i].x);
                    minY = Mathf.Min(minY, uv[i].y);
                }
                Rect pixels = new Rect(Mathf.Round(minX * source.texture.width), Mathf.Round(minY * source.texture.height), source.rect.width, source.rect.height);
                Sprite sliced = Sprite.Create(source.texture, pixels, new Vector2(0.5f, 0.5f), source.pixelsPerUnit, 0, SpriteMeshType.FullRect, new Vector4(240f, 0f, 240f, 0f));
                sliced.name = source.name + "_VerticalFixedCorners";
                frameSprites.Add(sliced);
                spriteProperty.SetValue(image, sliced, null);
                PropertyInfo imageType = image.GetType().GetProperty("type");
                imageType.SetValue(image, Enum.ToObject(imageType.PropertyType, 1), null);
                Debug.Log("[VerticalDialogue] Sliced frame " + source.name + " atlas=" + pixels + " fixedCorners=240");
            }

            private static Component NamedComponent(Transform t, string name)
            {
                Component[] components = t.GetComponents<Component>();
                for (int i = 0; i < components.Length; i++)
                    if (components[i] != null && components[i].GetType().Name == name) return components[i];
                throw new InvalidOperationException("Missing component " + name);
            }

            private static void SetScale(Transform t, Vector3 value)
            {
                if ((t.localScale - value).sqrMagnitude > 0.0001f) t.localScale = value;
            }

            private static void Set(RectTransform t, Vector2 min, Vector2 max, Vector2 pivot, Vector2 position, Vector2 size)
            {
                if ((t.anchorMin - min).sqrMagnitude > 0.0001f) t.anchorMin = min;
                if ((t.anchorMax - max).sqrMagnitude > 0.0001f) t.anchorMax = max;
                if ((t.pivot - pivot).sqrMagnitude > 0.0001f) t.pivot = pivot;
                if ((t.sizeDelta - size).sqrMagnitude > 0.0001f) t.sizeDelta = size;
                if ((t.anchoredPosition - position).sqrMagnitude > 0.0001f) t.anchoredPosition = position;
            }
        }
    }
}






