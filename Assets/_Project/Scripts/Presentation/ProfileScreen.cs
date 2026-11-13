using System;
using System.Collections.Generic;
using LiarsBatting.Core;
using LiarsBatting.Progression;
using UnityEngine;
using UnityEngine.UI;

namespace LiarsBatting.Presentation
{
    // The プロフィール screen, in tabs:
    //   プロフィール - my picture (a hero portrait), nickname, title, level bar, record against people,
    //              portrait picker and nickname change
    //   チャレンジ   - today's and this week's challenges with progress
    //   カスタマイズ - equip unlocked titles, portrait frames, background skins and card skins
    // Pure view: GameBootstrap does the saving through ProfileStore and reports back.
    public class ProfileScreen
    {
        public readonly GameObject Root;

        public Action OnBack;
        public Action<HeroId> OnIconPicked;
        public Action<string> OnRenameRequested;
        public Action<RewardInfo> OnEquipRequested;

        private static readonly Color Brass = new Color32(214, 160, 70, 255);
        private static readonly Color Slot = new Color(0.04f, 0.035f, 0.03f, 0.9f);

        private readonly List<GameObject> _pages = new List<GameObject>();
        private readonly List<Button> _tabButtons = new List<Button>();

        // profile tab
        private readonly Image _bigPortrait;
        private readonly Outline _bigPortraitFrame;
        private readonly Text _nicknameText;
        private readonly Text _titleText;
        private readonly Text _levelText;
        private readonly RectTransform _xpFill;
        private readonly Text _xpText;
        private readonly Text _recordText;
        private readonly Text _winRateText;
        private readonly Text _statusText;
        private readonly InputField _renameInput;
        private readonly Button _renameButton;
        private readonly Dictionary<HeroId, Outline> _iconHighlights = new Dictionary<HeroId, Outline>();
        private readonly List<Button> _iconButtons = new List<Button>();

        // challenges tab
        private readonly Text _dailyHeader;
        private readonly Text _weeklyHeader;
        private readonly List<ChallengeRow> _dailyRows = new List<ChallengeRow>();
        private readonly List<ChallengeRow> _weeklyRows = new List<ChallengeRow>();

        // cosmetics tab
        private readonly List<CosmeticOption> _cosmetics = new List<CosmeticOption>();
        private readonly Text _cosmeticsStatus;

        private class ChallengeRow
        {
            public Text Description, Progress, Reward;
            public Image Background;
        }

        private class CosmeticOption
        {
            public RewardInfo Reward;
            public Button Button;
            public Text Label;
        }

        // A HorizontalGroup with childForceExpandHeight reports itself as vertically flexible, so a
        // vertical layout around it would hand it a share of the spare height (the tab row grew huge).
        private static void FixedHeight(Component row, float height)
        {
            UiFactory.SetHeight(row, height);
            row.GetComponent<LayoutElement>().flexibleHeight = 0;
        }

        // Same problem sideways: a VerticalGroup reports itself as horizontally flexible, and its
        // preferred width follows its longest text, so columns would trade width with the content.
        private static void FixedWidth(Component column, float width)
        {
            UiFactory.SetWidth(column, width);
            column.GetComponent<LayoutElement>().flexibleWidth = 0;
        }

        // Takes whatever width is left, regardless of how long its text is.
        private static void FillRemainingWidth(Component column)
        {
            var le = column.GetComponent<LayoutElement>() ?? column.gameObject.AddComponent<LayoutElement>();
            le.minWidth = 0;
            le.preferredWidth = 0;
            le.flexibleWidth = 1;
        }

        // Selected = the highlighted plate with the amber border (same as the menu's primary buttons).
        private static void SetSelected(Button button, bool selected) => MenuButtonStyle.Apply(button, primary: selected);

        public ProfileScreen(Transform root)
        {
            Root = new GameObject("ProfileScreen", typeof(RectTransform));
            var rootRt = (RectTransform)Root.transform;
            rootRt.SetParent(root, false);
            rootRt.anchorMin = Vector2.zero;
            rootRt.anchorMax = Vector2.one;
            rootRt.offsetMin = Vector2.zero;
            rootRt.offsetMax = new Vector2(0, -56);   // below the header

            var panel = UiFactory.Panel(rootRt, "ProfilePanel", UITheme.Surface2);
            panel.anchorMin = new Vector2(0.5f, 0.5f);
            panel.anchorMax = new Vector2(0.5f, 0.5f);
            panel.pivot = new Vector2(0.5f, 0.5f);
            panel.sizeDelta = new Vector2(1040f, 600f);
            MenuButtonStyle.ApplyPanel(panel.GetComponent<Image>());

            var layout = UiFactory.VerticalGroup(panel, "Layout", spacing: 14, padding: new RectOffset(36, 36, 24, 24));
            UiFactory.StretchToFillParent(layout);

            // ---- tab row ----
            var tabRow = UiFactory.HorizontalGroup(layout, "Tabs", spacing: 10, childAlign: TextAnchor.MiddleLeft);
            FixedHeight(tabRow, 44);
            AddTab(tabRow, "プロフィール", 0);
            AddTab(tabRow, "ミッション", 1);
            AddTab(tabRow, "カスタマイズ", 2);
            AddTab(tabRow, "ヒーロー熟練度", 3);
            var tabSpacer = new GameObject("Spacer", typeof(RectTransform), typeof(LayoutElement));
            tabSpacer.transform.SetParent(tabRow, false);
            tabSpacer.GetComponent<LayoutElement>().flexibleWidth = 1;
            var back = UiFactory.Button(tabRow, "メインメニューへ", UITheme.Surface2, UITheme.Ink, () => OnBack?.Invoke(), 15);
            UiFactory.SetSize(back, 160, 44);
            MenuButtonStyle.Apply(back, primary: false);

            var pageHost = new GameObject("Pages", typeof(RectTransform), typeof(LayoutElement));
            pageHost.transform.SetParent(layout, false);
            pageHost.GetComponent<LayoutElement>().flexibleHeight = 1;

            // ================= プロフィール =================
            var profilePage = NewPage(pageHost.transform, "ProfilePage");
            var columns = UiFactory.HorizontalGroup(profilePage, "Columns", spacing: 40, childAlign: TextAnchor.UpperLeft);
            UiFactory.StretchToFillParent(columns);

            var left = UiFactory.VerticalGroup(columns, "Identity", spacing: 8, childAlign: TextAnchor.UpperCenter);
            FixedWidth(left, 300);

            // Centred row so the picture box keeps its own width (the column would stretch it to
            // 300 wide, and a frame drawn over a too-wide box doesn't fit the portrait).
            var frameRow = UiFactory.HorizontalGroup(left, "PortraitRow", spacing: 0, childAlign: TextAnchor.MiddleCenter);
            FixedHeight(frameRow, 206);
            var frame = UiFactory.Panel(frameRow, "PortraitFrame", Slot);
            UiFactory.SetSize(frame, 170, 206);
            _bigPortraitFrame = frame.gameObject.AddComponent<Outline>();
            _bigPortraitFrame.effectColor = Brass;
            _bigPortraitFrame.effectDistance = new Vector2(2f, -2f);
            _bigPortrait = NewImage(frame, "Portrait", new Vector2(8, 8), new Vector2(-8, -8));

            _nicknameText = UiFactory.Text(left, "", 22, UITheme.Ink, TextAnchor.MiddleCenter, FontStyle.Bold);
            UiFactory.SetHeight(_nicknameText, 30);
            AddShadow(_nicknameText);
            _titleText = UiFactory.Text(left, "", 14, Brass, TextAnchor.MiddleCenter, FontStyle.Italic);
            UiFactory.SetHeight(_titleText, 20);

            _levelText = UiFactory.Text(left, "", 16, UITheme.Ink, TextAnchor.MiddleCenter, FontStyle.Bold);
            UiFactory.SetHeight(_levelText, 22);
            var xpTrack = UiFactory.Panel(left, "XpTrack", Slot);
            UiFactory.SetHeight(xpTrack, 12);
            _xpFill = UiFactory.Panel(xpTrack, "XpFill", Brass);
            _xpFill.anchorMin = Vector2.zero;
            _xpFill.anchorMax = new Vector2(0, 1);
            _xpFill.offsetMin = Vector2.zero;
            _xpFill.offsetMax = Vector2.zero;
            _xpText = UiFactory.Text(left, "", 12, UITheme.Muted, TextAnchor.MiddleCenter);
            UiFactory.SetHeight(_xpText, 18);

            var divider = UiFactory.Panel(left, "Divider", UITheme.Border);
            UiFactory.SetHeight(divider, 1);
            var recordLabel = UiFactory.Text(left, "対人戦績", 13, UITheme.Muted, TextAnchor.MiddleCenter);
            UiFactory.SetHeight(recordLabel, 18);
            _recordText = UiFactory.Text(left, "", 24, Brass, TextAnchor.MiddleCenter, FontStyle.Bold);
            UiFactory.SetHeight(_recordText, 32);
            AddShadow(_recordText);
            _winRateText = UiFactory.Text(left, "", 13, UITheme.Muted, TextAnchor.MiddleCenter);
            UiFactory.SetHeight(_winRateText, 18);

            var right = UiFactory.VerticalGroup(columns, "Settings", spacing: 10, childAlign: TextAnchor.UpperLeft);
            FillRemainingWidth(right);

            AddLabel(right, "プロフィール画像");
            var grid = UiFactory.Grid(right, "IconGrid", columns: 4, cellSize: 96, spacing: 10);
            var gridLayout = grid.GetComponent<GridLayoutGroup>();
            gridLayout.cellSize = new Vector2(96, 118);
            gridLayout.childAlignment = TextAnchor.UpperLeft;
            UiFactory.SetHeight(grid, 118 * 2 + 10);
            foreach (var info in HeroCatalog.All) BuildIconCell(grid, info);

            AddLabel(right, "ニックネーム変更 (最大12文字)");
            var renameRow = UiFactory.HorizontalGroup(right, "RenameRow", spacing: 10);
            FixedHeight(renameRow, 48);
            _renameInput = UiFactory.InputField(renameRow, "新しいニックネーム");
            _renameInput.GetComponent<Image>().color = Slot;
            var inputOutline = _renameInput.gameObject.AddComponent<Outline>();
            inputOutline.effectColor = UITheme.Border;
            inputOutline.effectDistance = new Vector2(1.5f, -1.5f);
            UiFactory.SetFlexible(_renameInput, 1, 0);
            _renameButton = UiFactory.Button(renameRow, "変更", UITheme.Surface2, UITheme.Ink, () =>
            {
                string name = _renameInput.text.Trim();
                if (name.Length == 0) { SetStatus("新しいニックネームを入力してください。", true); return; }
                OnRenameRequested?.Invoke(name);
            }, 15);
            UiFactory.SetSize(_renameButton, 110, 48);
            MenuButtonStyle.Apply(_renameButton, primary: false);
            UiFactory.SubmitOnEnter(_renameInput, _renameButton);

            _statusText = UiFactory.Text(right, "", 13, UITheme.Muted, TextAnchor.MiddleLeft);
            UiFactory.SetHeight(_statusText, 20);

            // ================= ミッション =================
            var challengePage = NewPage(pageHost.transform, "ChallengePage");
            var challengeList = UiFactory.VerticalGroup(challengePage, "List", spacing: 8);
            UiFactory.StretchToFillParent(challengeList);
            _dailyHeader = AddLabel(challengeList, "");
            for (int i = 0; i < ChallengeCatalog.DailyCount; i++) _dailyRows.Add(BuildChallengeRow(challengeList));
            var gap = new GameObject("Gap", typeof(RectTransform), typeof(LayoutElement));
            gap.transform.SetParent(challengeList, false);
            gap.GetComponent<LayoutElement>().minHeight = 16;
            _weeklyHeader = AddLabel(challengeList, "");
            for (int i = 0; i < ChallengeCatalog.WeeklyCount; i++) _weeklyRows.Add(BuildChallengeRow(challengeList));

            // ================= カスタマイズ =================
            var cosmeticsPage = NewPage(pageHost.transform, "CosmeticsPage");
            var cosmeticsList = UiFactory.VerticalGroup(cosmeticsPage, "List", spacing: 8);
            UiFactory.StretchToFillParent(cosmeticsList);
            BuildCosmeticSection(cosmeticsList, "称号", RewardKind.Title);
            BuildCosmeticSection(cosmeticsList, "肖像画フレーム", RewardKind.Frame);
            BuildCosmeticSection(cosmeticsList, "背景スキン", RewardKind.Background);
            BuildCosmeticSection(cosmeticsList, "カードスキン", RewardKind.CardSkin);
            _cosmeticsStatus = UiFactory.Text(cosmeticsList, "", 13, UITheme.Muted, TextAnchor.MiddleLeft);
            UiFactory.SetHeight(_cosmeticsStatus, 20);

            // ================= ヒーロー熟練度 =================
            BuildHeroPage(NewPage(pageHost.transform, "HeroPage"));

            SelectTab(0);
            Root.SetActive(false);
        }

        // ---------- building blocks ----------

        private void AddTab(Transform row, string label, int index)
        {
            var button = UiFactory.Button(row, label, UITheme.Surface2, UITheme.Ink, () => SelectTab(index), 15);
            UiFactory.SetSize(button, 130, 44);
            MenuButtonStyle.Apply(button, primary: false);
            _tabButtons.Add(button);
        }

        private RectTransform NewPage(Transform host, string name)
        {
            var page = new GameObject(name, typeof(RectTransform));
            var rt = (RectTransform)page.transform;
            rt.SetParent(host, false);
            UiFactory.StretchToFillParent(rt);
            _pages.Add(page);
            return rt;
        }

        public void SelectTab(int index)
        {
            for (int i = 0; i < _pages.Count; i++) _pages[i].SetActive(i == index);
            for (int i = 0; i < _tabButtons.Count; i++) SetSelected(_tabButtons[i], i == index);
        }

        private static Text AddLabel(Transform parent, string text)
        {
            var label = UiFactory.Text(parent, text, 15, Brass, TextAnchor.MiddleLeft, FontStyle.Bold);
            UiFactory.SetHeight(label, 24);
            return label;
        }

        private static Image NewImage(Transform parent, string name, Vector2 offsetMin, Vector2 offsetMax)
        {
            var go = new GameObject(name, typeof(RectTransform), typeof(Image));
            var rt = (RectTransform)go.transform;
            rt.SetParent(parent, false);
            UiFactory.StretchToFillParent(rt);
            rt.offsetMin = offsetMin;
            rt.offsetMax = offsetMax;
            var image = go.GetComponent<Image>();
            image.preserveAspect = true;
            image.raycastTarget = false;
            return image;
        }

        private void BuildIconCell(Transform grid, HeroInfo info)
        {
            var hero = info.Id;
            var cell = new GameObject($"Icon_{hero}", typeof(RectTransform), typeof(Image), typeof(Button));
            cell.transform.SetParent(grid, false);
            var bg = cell.GetComponent<Image>();
            bg.color = Slot;
            var button = cell.GetComponent<Button>();
            button.targetGraphic = bg;
            button.onClick.AddListener(() => GameAudio.Play("SFX_UI_Click", volume: 0.6f));
            button.onClick.AddListener(() => OnIconPicked?.Invoke(hero));
            cell.AddComponent<ButtonHoverSound>();
            _iconButtons.Add(button);

            var highlight = cell.AddComponent<Outline>();
            highlight.effectColor = Brass;
            highlight.effectDistance = new Vector2(3f, -3f);
            highlight.enabled = false;
            _iconHighlights[hero] = highlight;

            NewImage(cell.transform, "Portrait", new Vector2(6, 22), new Vector2(-6, -4)).sprite = PortraitOf(hero);

            var name = UiFactory.Text(cell.transform, info.Name, 12, UITheme.Ink, TextAnchor.MiddleCenter);
            name.raycastTarget = false;
            var nameRt = (RectTransform)name.transform;
            nameRt.anchorMin = new Vector2(0, 0);
            nameRt.anchorMax = new Vector2(1, 0);
            nameRt.pivot = new Vector2(0.5f, 0);
            nameRt.anchoredPosition = new Vector2(0, 2);
            nameRt.sizeDelta = new Vector2(0, 20);
        }

        private static ChallengeRow BuildChallengeRow(Transform parent, float height = 46, int fontSize = 15)
        {
            var rowRt = UiFactory.HorizontalGroup(parent, "Challenge", spacing: 16,
                padding: new RectOffset(16, 16, 0, 0), childAlign: TextAnchor.MiddleLeft);
            FixedHeight(rowRt, height);
            var bg = rowRt.gameObject.AddComponent<Image>();
            bg.color = Slot;
            var row = new ChallengeRow { Background = bg };
            row.Description = UiFactory.Text(rowRt, "", fontSize, UITheme.Ink, TextAnchor.MiddleLeft);
            UiFactory.SetFlexible(row.Description, 1, 0);
            row.Progress = UiFactory.Text(rowRt, "", fontSize, UITheme.Ink, TextAnchor.MiddleRight, FontStyle.Bold);
            UiFactory.SetWidth(row.Progress, 90);
            row.Reward = UiFactory.Text(rowRt, "", fontSize - 1, Brass, TextAnchor.MiddleRight, FontStyle.Bold);
            UiFactory.SetWidth(row.Reward, 100);
            return row;
        }

        // ---------- hero mastery tab ----------

        private HeroId _selectedHero = HeroId.Hunter;
        private PlayerProfile _lastProfile;
        private readonly Dictionary<HeroId, (Outline highlight, Text label)> _heroEntries =
            new Dictionary<HeroId, (Outline, Text)>();
        private Image _heroPreview;
        private Text _heroName, _heroAbility, _heroLevel, _heroXpText;
        private RectTransform _heroXpFill;
        private readonly List<ChallengeRow> _heroRows = new List<ChallengeRow>();
        private readonly List<(RewardKind kind, List<CosmeticOption> options)> _heroEquipRows =
            new List<(RewardKind, List<CosmeticOption>)>();

        private void BuildHeroPage(Transform page)
        {
            var columns = UiFactory.HorizontalGroup(page, "HeroColumns", spacing: 24, childAlign: TextAnchor.UpperLeft);
            UiFactory.StretchToFillParent(columns);

            // left: the seven heroes
            var list = UiFactory.VerticalGroup(columns, "HeroList", spacing: 6);
            FixedWidth(list, 200);
            foreach (var info in HeroCatalog.All)
            {
                var hero = info.Id;
                var entry = new GameObject($"Hero_{hero}", typeof(RectTransform), typeof(Image), typeof(Button));
                entry.transform.SetParent(list, false);
                UiFactory.SetHeight(entry.transform, 60);
                var bg = entry.GetComponent<Image>();
                bg.color = Slot;
                var button = entry.GetComponent<Button>();
                button.targetGraphic = bg;
                button.onClick.AddListener(() => GameAudio.Play("SFX_UI_Click", volume: 0.6f));
                button.onClick.AddListener(() => SelectHero(hero));
                entry.AddComponent<ButtonHoverSound>();
                var highlight = entry.AddComponent<Outline>();
                highlight.effectColor = Brass;
                highlight.effectDistance = new Vector2(2f, -2f);
                highlight.enabled = false;

                var icon = NewImage(entry.transform, "Portrait", Vector2.zero, Vector2.zero);
                var iconRt = (RectTransform)icon.transform;
                iconRt.anchorMin = new Vector2(0, 0);
                iconRt.anchorMax = new Vector2(0, 1);
                iconRt.pivot = new Vector2(0, 0.5f);
                iconRt.offsetMin = new Vector2(6, 4);
                iconRt.offsetMax = new Vector2(52, -4);
                icon.sprite = PortraitOf(hero);

                var label = UiFactory.Text(entry.transform, "", 15, UITheme.Ink, TextAnchor.MiddleLeft, FontStyle.Bold);
                label.raycastTarget = false;
                var labelRt = (RectTransform)label.transform;
                UiFactory.StretchToFillParent(labelRt);
                labelRt.offsetMin = new Vector2(62, 0);
                labelRt.offsetMax = new Vector2(-8, 0);
                _heroEntries[hero] = (highlight, label);
            }

            // right: the selected hero's mastery
            var detail = UiFactory.VerticalGroup(columns, "HeroDetail", spacing: 6);
            FillRemainingWidth(detail);

            var header = UiFactory.HorizontalGroup(detail, "Header", spacing: 16, childAlign: TextAnchor.MiddleLeft);
            FixedHeight(header, 118);
            var previewFrame = UiFactory.Panel(header, "PreviewFrame", Slot);
            UiFactory.SetSize(previewFrame, 92, 112);
            _heroPreview = NewImage(previewFrame, "Preview", new Vector2(4, 4), new Vector2(-4, -4));
            var infoColumn = UiFactory.VerticalGroup(header, "Info", spacing: 2);
            UiFactory.SetFlexible(infoColumn, 1, 0);
            _heroName = UiFactory.Text(infoColumn, "", 20, UITheme.Ink, TextAnchor.MiddleLeft, FontStyle.Bold);
            UiFactory.SetHeight(_heroName, 26);
            AddShadow(_heroName);
            _heroAbility = UiFactory.Text(infoColumn, "", 13, Brass, TextAnchor.MiddleLeft, FontStyle.Bold);
            UiFactory.SetHeight(_heroAbility, 34);   // ability name + description may take two lines
            _heroLevel = UiFactory.Text(infoColumn, "", 15, UITheme.Ink, TextAnchor.MiddleLeft, FontStyle.Bold);
            UiFactory.SetHeight(_heroLevel, 20);
            var xpTrack = UiFactory.Panel(infoColumn, "XpTrack", Slot);
            UiFactory.SetHeight(xpTrack, 10);
            _heroXpFill = UiFactory.Panel(xpTrack, "XpFill", Brass);
            _heroXpFill.anchorMin = Vector2.zero;
            _heroXpFill.anchorMax = new Vector2(0, 1);
            _heroXpFill.offsetMin = Vector2.zero;
            _heroXpFill.offsetMax = Vector2.zero;
            _heroXpText = UiFactory.Text(infoColumn, "", 12, UITheme.Muted, TextAnchor.MiddleLeft);
            UiFactory.SetHeight(_heroXpText, 16);

            var challengeLabel = UiFactory.Text(detail, $"ヒーローミッション  ·  達成ごとに熟練度 +{LevelTable.HeroChallengeXp} XP",
                14, Brass, TextAnchor.MiddleLeft, FontStyle.Bold);
            UiFactory.SetHeight(challengeLabel, 22);
            for (int i = 0; i < HeroChallengeCatalog.PerHero; i++) _heroRows.Add(BuildChallengeRow(detail, 30, 14));

            BuildHeroEquipRow(detail, "称号", RewardKind.Title, 2);
            BuildHeroEquipRow(detail, "肖像画", RewardKind.HeroSkin, 3);
            BuildHeroEquipRow(detail, "エフェクト", RewardKind.HeroEffect, 2);
        }

        private void BuildHeroEquipRow(Transform parent, string title, RewardKind kind, int count)
        {
            var row = UiFactory.HorizontalGroup(parent, title, spacing: 8, childAlign: TextAnchor.MiddleLeft);
            FixedHeight(row, 42);
            var label = UiFactory.Text(row, title, 14, Brass, TextAnchor.MiddleLeft, FontStyle.Bold);
            UiFactory.SetWidth(label, 64);
            var options = new List<CosmeticOption>();
            for (int i = 0; i < count; i++)
            {
                var option = new CosmeticOption();
                option.Button = UiFactory.Button(row, "", UITheme.Surface2, UITheme.Ink,
                    () => { if (option.Reward != null) OnEquipRequested?.Invoke(option.Reward); }, 13);
                UiFactory.SetSize(option.Button, 170, 40);
                MenuButtonStyle.Apply(option.Button, primary: false);
                option.Label = option.Button.GetComponentInChildren<Text>();
                options.Add(option);
            }
            _heroEquipRows.Add((kind, options));
        }

        public void SelectHero(HeroId hero)
        {
            _selectedHero = hero;
            if (_lastProfile != null) FillHeroPage(_lastProfile.Progress);
        }

        private void FillHeroPage(PlayerProgress p)
        {
            foreach (var pair in _heroEntries)
            {
                pair.Value.highlight.enabled = pair.Key == _selectedHero;
                pair.Value.label.text = $"{HeroCatalog.Get(pair.Key).Name}\n<size=12>Lv.{p.HeroLevel(pair.Key)}</size>";
            }

            var hero = _selectedHero;
            var info = HeroCatalog.Get(hero);
            var hp = p.Heroes.TryGetValue(hero.ToString(), out var found) ? found : new HeroProgress();
            PortraitLook.Apply(_heroPreview, hero, p);
            _heroName.text = info.Name;
            _heroAbility.text = $"{info.AbilityName}  —  {info.AbilityDescription}";
            bool maxed = hp.Level >= LevelTable.MaxHeroLevel;
            int need = LevelTable.HeroXpToNext(hp.Level);
            _heroLevel.text = $"熟練度 Lv.{hp.Level}";
            _heroXpFill.anchorMax = new Vector2(maxed ? 1f : Mathf.Clamp01((float)hp.Xp / need), 1f);
            _heroXpText.text = maxed ? "最高熟練度" : $"{hp.Xp} / {need} XP";

            FillRows(_heroRows, HeroChallengeCatalog.For(hero), hp.Challenges, hp.Done, LevelTable.HeroChallengeXp);

            foreach (var (kind, options) in _heroEquipRows)
            {
                var rewards = RewardCatalog.ForHero(hero, kind);
                rewards.Sort((a, b) => a.Level.CompareTo(b.Level));
                for (int i = 0; i < options.Count; i++)
                {
                    var option = options[i];
                    option.Reward = i < rewards.Count ? rewards[i] : null;
                    option.Button.gameObject.SetActive(option.Reward != null);
                    if (option.Reward == null) continue;
                    bool unlocked = p.IsUnlocked(option.Reward);
                    option.Button.interactable = unlocked;
                    option.Label.text = unlocked ? option.Reward.Name : $"{option.Reward.Name}\nLv.{option.Reward.Level} 解放";
                    SetSelected(option.Button, p.IsEquipped(option.Reward));
                }
            }
        }

        private void BuildCosmeticSection(Transform parent, string title, RewardKind kind)
        {
            AddLabel(parent, title);
            var row = UiFactory.HorizontalGroup(parent, title, spacing: 10, childAlign: TextAnchor.MiddleLeft);
            FixedHeight(row, 52);
            foreach (var reward in RewardCatalog.OfKind(kind))
            {
                var r = reward;
                var button = UiFactory.Button(row, r.Name, UITheme.Surface2, UITheme.Ink, () => OnEquipRequested?.Invoke(r), 14);
                UiFactory.SetSize(button, 170, 50);
                MenuButtonStyle.Apply(button, primary: false);
                _cosmetics.Add(new CosmeticOption
                {
                    Reward = r, Button = button, Label = button.GetComponentInChildren<Text>()
                });
            }
        }

        public static Sprite PortraitOf(HeroId hero) => Resources.Load<Sprite>($"Heroes/Hero_{hero}_Portrait");

        public static Color TintOf(RewardInfo reward, Color fallback) =>
            reward != null && !string.IsNullOrEmpty(reward.Tint) ? UITheme.Hex(reward.Tint) : fallback;

        // ---------- filling ----------

        // Shown right away with what's known (the nickname) while the profile loads.
        public void ShowLoading(string nickname)
        {
            _nicknameText.text = nickname;
            _titleText.text = "";
            _levelText.text = "";
            _xpText.text = "";
            _recordText.text = "…";
            _winRateText.text = "読み込み中";
            _renameInput.text = "";
            SetStatus("", false);
            _cosmeticsStatus.text = "";
            SetInteractable(false);
        }

        public void ShowProfile(PlayerProfile profile)
        {
            var p = profile.Progress;
            ProgressionService.RollPeriods(p, DateTime.UtcNow);   // show today's set even before a match

            _nicknameText.text = profile.Nickname;
            _titleText.text = RewardCatalog.TitleText(p.Title);
            // Frame art goes over the portrait itself; the box keeps its thin brass edge. A frame
            // without art falls back to colouring that edge.
            bool frameArt = PortraitFrame.Apply(_bigPortrait, p.Frame);
            bool tinted = !frameArt && p.Frame != RewardCatalog.NoFrame;
            _bigPortraitFrame.effectColor = tinted ? TintOf(RewardCatalog.Find(p.Frame), Brass) : Brass;
            _bigPortraitFrame.effectDistance = tinted ? new Vector2(4f, -4f) : new Vector2(2f, -2f);

            bool maxed = p.Level >= LevelTable.MaxPlayerLevel;
            int need = LevelTable.PlayerXpToNext(p.Level);
            _levelText.text = $"Lv.{p.Level}";
            _xpFill.anchorMax = new Vector2(maxed ? 1f : Mathf.Clamp01((float)p.Xp / need), 1f);
            _xpText.text = maxed ? "最高レベル" : $"{p.Xp} / {need} XP";

            _recordText.text = profile.RecordText;
            _winRateText.text = profile.HumanGames == 0
                ? "対人戦の記録がまだありません"
                : $"勝率 {profile.WinRatePercent}% · 全 {profile.HumanGames}試合";
            SetSelectedIcon(profile.ProfileIcon);

            var now = DateTime.UtcNow;
            _dailyHeader.text = $"デイリーミッション  ·  リセットまで {FormatSpan(ChallengeCatalog.UntilDailyReset(now))}";
            FillRows(_dailyRows, ChallengeCatalog.DailyFor(p.DailyPeriod), p.Daily, p.DailyDone, LevelTable.DailyChallengeXp);
            _weeklyHeader.text = $"ウィークリーミッション  ·  リセットまで {FormatSpan(ChallengeCatalog.UntilWeeklyReset(now))}";
            FillRows(_weeklyRows, ChallengeCatalog.WeeklyFor(p.WeeklyPeriod), p.Weekly, p.WeeklyDone, LevelTable.WeeklyChallengeXp);

            foreach (var option in _cosmetics)
            {
                bool unlocked = p.IsUnlocked(option.Reward);
                option.Button.interactable = unlocked;
                option.Label.text = unlocked ? option.Reward.Name : $"{option.Reward.Name}\nLv.{option.Reward.Level} 解放";
                SetSelected(option.Button, p.IsEquipped(option.Reward));
            }

            _lastProfile = profile;
            FillHeroPage(p);
            SetInteractable(true);
        }

        private static void FillRows(List<ChallengeRow> rows, List<ChallengeDef> defs, Dictionary<string, int> progress,
            List<string> done, int xp)
        {
            for (int i = 0; i < rows.Count; i++)
            {
                var row = rows[i];
                bool has = i < defs.Count;
                row.Background.gameObject.SetActive(has);
                if (!has) continue;
                var def = defs[i];
                bool complete = done.Contains(def.Id);
                progress.TryGetValue(def.Id, out int value);
                row.Description.text = def.Text;
                row.Description.color = complete ? UITheme.Muted : UITheme.Ink;
                row.Progress.text = complete ? "達成" : $"{value} / {def.Target}";
                row.Progress.color = complete ? Brass : UITheme.Ink;
                row.Reward.text = $"+{xp} XP";
            }
        }

        private static string FormatSpan(TimeSpan span)
        {
            if (span.TotalDays >= 1) return $"{(int)span.TotalDays}日 {span.Hours}時間";
            if (span.TotalHours >= 1) return $"{(int)span.TotalHours}時間 {span.Minutes}分";
            return $"{Math.Max(1, span.Minutes)}分";
        }

        public void SetSelectedIcon(HeroId hero)
        {
            _bigPortrait.sprite = PortraitOf(hero);
            foreach (var pair in _iconHighlights) pair.Value.enabled = pair.Key == hero;
        }

        public void SetInteractable(bool interactable)
        {
            foreach (var b in _iconButtons) b.interactable = interactable;
            _renameButton.interactable = interactable;
        }

        public void ClearRenameInput() => _renameInput.text = "";

        public void SetStatus(string message, bool isError)
        {
            _statusText.text = message;
            _statusText.color = isError ? UITheme.Clay : UITheme.Muted;
        }

        public void SetCosmeticsStatus(string message, bool isError)
        {
            _cosmeticsStatus.text = message;
            _cosmeticsStatus.color = isError ? UITheme.Clay : UITheme.Muted;
        }

        private static void AddShadow(Text text)
        {
            var shadow = text.gameObject.AddComponent<Shadow>();
            shadow.effectColor = new Color(0f, 0f, 0f, 0.85f);
            shadow.effectDistance = new Vector2(1.5f, -2f);
        }
    }
}
