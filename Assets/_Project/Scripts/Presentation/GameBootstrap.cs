using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using LiarsBatting.Core;
using LiarsBatting.AI;
using LiarsBatting.Network;
using LiarsBatting.Progression;
using Newtonsoft.Json.Linq;

namespace LiarsBatting.Presentation
{
    // Boots the entire game with zero scene setup: drop this script anywhere in
    // Assets/_Project/Scripts/Presentation and press Play on an empty scene.
    // [RuntimeInitializeOnLoadMethod] builds its own GameObject, Canvas and
    // EventSystem the moment the scene loads, so there is nothing to wire by hand.
    public class GameBootstrap : MonoBehaviour
    {
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void Boot()
        {
            var go = new GameObject("~GameBootstrap");
            go.AddComponent<GameBootstrap>();
        }

        private const string NicknameKey = "lb_nickname";

        // Countdown durations (seconds) and their timeout fallback, per spec:
        // 0 hero pick -> random hero, 1 secret pick -> random secret,
        // 2 guess submit -> random guess, 3 defend choice -> truth,
        // 4 trust/challenge -> trust.
        private const float HeroPickSeconds = 10f;
        private const float SecretPickSeconds = 10f;
        private const float GuessSeconds = 30f;
        private const float DefendChoiceSeconds = 20f;
        private const float TrustChoiceSeconds = 20f;
        private const float RogueAnswerTimeoutSeconds = 10f;   // online: wait for the opponent's insight answer

        // Single-player pacing: short beats so the opponent doesn't answer / attack instantly.
        private const float AiJudgeDelaySeconds = 1.2f;       // opponent "judging" my guess
        private const float AiGuessDelaySeconds = 1.8f;       // opponent "thinking" before its guess arrives
        private const float TurnHandoverDelaySeconds = 0.9f;  // pause before my own turn starts again

        // ParrelSync clones share the ORIGINAL project's Editor PlayerPrefs by
        // default (same company/product name), so without this both windows
        // would show the same nickname -- defeating the whole point of testing
        // matchmaking against yourself. Give the clone its own key. This has no
        // effect on a real build, where ParrelSync isn't even compiled in.
        private static string EffectiveNicknameKey()
        {
#if UNITY_EDITOR
            if (ParrelSync.ClonesManager.IsClone()) return NicknameKey + "_clone";
#endif
            return NicknameKey;
        }

        private GameState _state;
        private AiOpponent _ai;
        private string _nickname;
        private FirestoreClient _firestore;
        private MatchmakingService _matchmaking;
        private MatchmakingService _inviteWatcher;

        private NetworkMatchController _network;
        private bool _isOnlineMatch;
        private int[] _myLastOnlineGuess;

        private GameObject _nicknameScreen;
        private Text _nicknameTitle;
        private Text _nicknameSubtitle;
        private Text _nicknameStatusText;
        private InputField _nicknameInput;
        private Button _nicknameCancelButton;
        private Button _nicknameConfirmButton;
        private GameObject _mainMenuScreen;
        private GameObject _randomMatchScreen;
        private GameObject _friendMatchScreen;
        private GameObject _heroSelectScreen;
        private GameObject _setupScreen;
        private GameObject _matchScreen;
        private GameObject _gameOverScreen;

        private Text _randomMatchStatusText;
        private Text _randomMatchWaitText;
        private readonly Image[] _randomSearchCards = new Image[4];
        private float _randomSearchStart;
        private InputField _friendTargetInput;
        private Text _friendStatusText;
        private Button _friendInviteButton;

        private readonly List<Button> _heroSelectButtons = new List<Button>();
        private readonly Dictionary<HeroId, Text> _heroSelectLevels = new Dictionary<HeroId, Text>();
        private readonly Dictionary<HeroId, Image> _heroSelectPortraits = new Dictionary<HeroId, Image>();
        private PlayerProgress _opponentProgress;       // online opponent's progress (their portrait skin/effect)

        private CardPickerView _setupPicker;
        private CardPickerView _attackPicker;
        private MatchHudView _statusPanel;
        private ChoiceOverlayView _choiceOverlay;
        private bool _awaitingMyChallengeOutcome;   // online: I challenged the opponent's answer and the verdict isn't in yet
        private bool _matchOver;                    // a result popup has been shown for this match: ignore anything that arrives later
        private bool _defendingNow;                 // online: I'm in the middle of answering the opponent's guess
        private string _pendingNotice;              // online: banner to show (before the turn banner) once the turn change is processed
        private float _pendingNoticeHold;
        private int _pendingNoticeDigit = -1;
        private CountdownTimerView _timer;
        private HeroPortraitView _myHeroPortrait;
        private HeroPortraitView _opponentHeroPortrait;
        private bool _myTurnActive;   // true while it's my attack turn (gates the Priest ability button)

        private Text _headerText;
        private GameObject _turnAnnouncementRoot;
        private Image _turnAnnouncementBackdrop;
        private Text _turnAnnouncementText;
        private Image _turnAnnouncementCard;
        private Coroutine _turnAnnouncementRoutine;
        private Text _tokenText;
        private Button _surrenderButton;
        private Button _headerSettingsButton;
        private ResultPopupView _resultPopup;
        private Text _menuGreetingText;
        private Image _menuProfileIcon;
        private ProfileStore _profiles;
        private ProfileScreen _profileScreen;
        private Image _rootBackground;                  // the table background (tinted by the equipped background skin)
        private string _opponentTitle = "";             // online opponent's equipped title, shown under their portrait
        private MatchStats _stats = new MatchStats();   // what I did this match, for XP and challenges
        private bool _lastDefenseWasLie;                // my most recent report used a LIE TOKEN
        private bool _nextOnlineTurnExtra;              // online: my next attack turn is a kept (Warrior) turn
        private float _matchStartTime;                  // Time.unscaledTime when the match screen opened
        private bool _matchClockRunning;
        private GameObject _soundSettings;
        private Slider _musicSlider;
        private Slider _effectsSlider;
        private Toggle _musicMute;
        private Toggle _effectsMute;

        private void Start()
        {
            GameAudio.Initialize();
            UiFactory.EnsureEventSystem();
            var canvas = UiFactory.CreateCanvas();
            DontDestroyOnLoad(canvas.gameObject);

            var root = UiFactory.FullScreen(canvas.transform, "Root", Color.white);
            var rootImage = root.GetComponent<Image>();
            rootImage.sprite = CardArt.Background();   // underground table background
            rootImage.raycastTarget = false;
            _rootBackground = rootImage;
            _firestore = new FirestoreClient(this);
            _profiles = new ProfileStore(_firestore);

            BuildHeader(root);
            BuildNicknameScreen(root);
            BuildMainMenuScreen(root);
            BuildProfileScreen(root);
            BuildRandomMatchScreen(root);
            BuildFriendMatchScreen(root);
            BuildHeroSelectScreen(root);
            BuildSetupScreen(root);
            BuildMatchScreen(root);
            BuildGameOverScreen(root);
            _choiceOverlay = new ChoiceOverlayView(root); // built last so it renders on top
            _choiceOverlay.SetHistorySources(_statusPanel.MyAttackHistory, _statusPanel.OpponentAttackHistory, this);

            _nickname = PlayerPrefs.GetString(EffectiveNicknameKey(), "");
            if (string.IsNullOrEmpty(_nickname)) ShowNicknameScreen();
            else ShowMainMenu();
        }

        private void Update()
        {
            // Label under the hourglass ("自分のターン" / "相手のターン") follows the header text.
            if (_statusPanel != null && _headerText != null)
                _statusPanel.SetTurnLabel(_headerText.text);
            if (_matchClockRunning) _statusPanel.SetElapsed(Time.unscaledTime - _matchStartTime);
            if (_randomMatchScreen != null && _randomMatchScreen.activeSelf) AnimateRandomSearch();
        }

        // The match clock above the hourglass: runs from the moment the match screen opens until
        // the match ends (the final time stays on screen under the result popup).
        private void StartMatchClock()
        {
            _matchStartTime = Time.unscaledTime;
            _matchClockRunning = true;
            _statusPanel.SetElapsed(0f);
        }

        private float StopMatchClock()
        {
            float elapsed = _matchClockRunning ? Time.unscaledTime - _matchStartTime : 0f;
            _matchClockRunning = false;
            return elapsed;
        }

        // ---------- layout ----------

        private void BuildHeader(Transform root)
        {
            var header = UiFactory.Panel(root, "Header", UITheme.Surface);
            header.anchorMin = new Vector2(0, 1);
            header.anchorMax = new Vector2(1, 1);
            header.pivot = new Vector2(0.5f, 1);
            header.sizeDelta = new Vector2(0, 56);
            header.anchoredPosition = Vector2.zero;

            var row = UiFactory.HorizontalGroup(header, "HeaderRow", spacing: 16,
                padding: new RectOffset(20, 20, 12, 12));
            row.anchorMin = Vector2.zero;
            row.anchorMax = Vector2.one;
            row.offsetMin = Vector2.zero;
            row.offsetMax = Vector2.zero;

            _headerText = UiFactory.Text(row, "ライアーズ・バッティング", 18, UITheme.Ink, TextAnchor.MiddleLeft, FontStyle.Bold);
            UiFactory.SetFlexible(_headerText, 1, 0);
            // The countdown number is placed relative to the full-screen root (centred under the
            // header), not inside the header row.
            _timer = new CountdownTimerView(root, this);
            _tokenText = UiFactory.Text(row, "", 13, UITheme.Muted, TextAnchor.MiddleRight);
            UiFactory.SetSize(_tokenText, 220, 30);

            _surrenderButton = UiFactory.Button(row, "降伏", UITheme.Clay, Color.white, OnSurrenderClicked, 13);
            UiFactory.SetSize(_surrenderButton, 70, 30);
            _surrenderButton.gameObject.SetActive(false); // only shown while a match is actually underway

            // The main menu has its own settings button; this one covers hero pick, secret pick
            // and the match itself, so volume can be changed without leaving the game.
            _headerSettingsButton = UiFactory.Button(row, "設定", UITheme.Surface2, UITheme.Ink, ShowSoundSettings, 13);
            UiFactory.SetSize(_headerSettingsButton, 70, 30);
            _headerSettingsButton.gameObject.SetActive(false);
        }

        private GameObject BuildFullScreenContainer(Transform root, string name)
        {
            var go = new GameObject(name, typeof(RectTransform));
            var rt = (RectTransform)go.transform;
            rt.SetParent(root, false);
            rt.anchorMin = Vector2.zero;
            rt.anchorMax = Vector2.one;
            rt.offsetMin = Vector2.zero;
            rt.offsetMax = new Vector2(0, -56);
            return go;
        }

        // Same look as the rest of the lobby: the brass-bordered dark plate of the choice /
        // sound-settings popups, iron-plate buttons, shadowed text. Used both for the very
        // first nickname and for "ニックネーム変更" from the main menu (ShowNicknameScreen
        // switches the title and shows the cancel button only in that second case).
        private void BuildNicknameScreen(Transform root)
        {
            _nicknameScreen = BuildFullScreenContainer(root, "NicknameScreen");

            var panel = UiFactory.Panel(_nicknameScreen.transform, "NicknamePanel", UITheme.Surface2);
            panel.anchorMin = new Vector2(0.5f, 0.5f);
            panel.anchorMax = new Vector2(0.5f, 0.5f);
            panel.pivot = new Vector2(0.5f, 0.5f);
            panel.sizeDelta = new Vector2(520f, 350f);
            panel.anchoredPosition = new Vector2(0f, 10f);
            MenuButtonStyle.ApplyPanel(panel.GetComponent<Image>());

            var content = UiFactory.VerticalGroup(panel, "Content", spacing: 12,
                padding: new RectOffset(44, 44, 34, 30), childAlign: TextAnchor.UpperCenter);
            UiFactory.StretchToFillParent(content);

            _nicknameTitle = UiFactory.Text(content, "", 24, UITheme.Ink, TextAnchor.MiddleCenter, FontStyle.Bold);
            UiFactory.SetHeight(_nicknameTitle, 34);
            AddTextShadow(_nicknameTitle);
            _nicknameSubtitle = UiFactory.Text(content, "", 13, UITheme.Muted, TextAnchor.MiddleCenter);
            UiFactory.SetHeight(_nicknameSubtitle, 22);

            var divider = UiFactory.Panel(content, "Divider", UITheme.Border);
            UiFactory.SetHeight(divider, 1);
            divider.GetComponent<Image>().raycastTarget = false;

            var fieldLabel = UiFactory.Text(content, "ニックネーム (最大12文字)", 13, new Color32(214, 160, 70, 255),
                TextAnchor.MiddleLeft, FontStyle.Bold);
            UiFactory.SetHeight(fieldLabel, 20);

            var input = UiFactory.InputField(content, "例：ライアー");
            _nicknameInput = input;
            var inputImage = input.GetComponent<Image>();
            inputImage.color = new Color(0.04f, 0.035f, 0.03f, 0.9f);   // recessed slot inside the plate
            var inputOutline = input.gameObject.AddComponent<Outline>();
            inputOutline.effectColor = UITheme.Border;
            inputOutline.effectDistance = new Vector2(1.5f, -1.5f);
            UiFactory.SetHeight(input, 50);

            var statusText = UiFactory.Text(content, "", 12, UITheme.Clay, TextAnchor.MiddleCenter);
            UiFactory.SetHeight(statusText, 20);
            statusText.text = "";
            _nicknameStatusText = statusText;

            var buttonRow = UiFactory.HorizontalGroup(content, "Buttons", spacing: 12, childAlign: TextAnchor.MiddleCenter);
            UiFactory.SetHeight(buttonRow, 52);

            _nicknameCancelButton = UiFactory.Button(buttonRow, "キャンセル", UITheme.Surface2, UITheme.Ink,
                ShowMainMenu, 15);
            UiFactory.SetSize(_nicknameCancelButton, 150, 52);
            MenuButtonStyle.Apply(_nicknameCancelButton, primary: false);

            Button confirmBtn = null;
            confirmBtn = UiFactory.Button(buttonRow, "確認", UITheme.Accent, Color.white, () =>
            {
                string name = input.text.Trim();
                string problem = ProfileStore.NicknameProblem(name);
                if (problem != null)
                {
                    ShowNicknameStatus(statusText, problem, isError: true);
                    return;
                }
                if (name == _nickname)
                {
                    // Changing to the name you already have: nothing to register.
                    ShowMainMenu();
                    return;
                }

                confirmBtn.interactable = false;
                ShowNicknameStatus(statusText, "確認中...", isError: false);
                RegisterNickname(name, confirmBtn, statusText);
            }, 16);
            UiFactory.SetSize(confirmBtn, 220, 52);
            MenuButtonStyle.Apply(confirmBtn, primary: true);
            _nicknameConfirmButton = confirmBtn;
            UiFactory.SubmitOnEnter(input, confirmBtn);
        }

        private static void AddTextShadow(Text text)
        {
            var shadow = text.gameObject.AddComponent<Shadow>();
            shadow.effectColor = new Color(0f, 0f, 0f, 0.85f);
            shadow.effectDistance = new Vector2(1.5f, -2f);
        }

        private void RegisterNickname(string name, Button confirmBtn, Text statusText)
        {
            // Create-only: if two players register the same name at the same moment, only one wins.
            _profiles.Register(name,
                onRegistered: () =>
                {
                    _nickname = name;
                    PlayerPrefs.SetString(EffectiveNicknameKey(), _nickname);
                    PlayerPrefs.Save();
                    ShowMainMenu();
                },
                onError: error =>
                {
                    confirmBtn.interactable = true;
                    ShowNicknameStatus(statusText, error, isError: true);
                });
        }

        private void ShowNicknameStatus(Text statusText, string message, bool isError)
        {
            statusText.text = message;
            statusText.color = isError ? UITheme.Clay : UITheme.Muted;
        }

        private void BuildMainMenuScreen(Transform root)
        {
            _mainMenuScreen = BuildFullScreenContainer(root, "MainMenuScreen");

            // The wordmark above the menu (Resources/Logo). If the art isn't there -- e.g. Unity hasn't
            // imported it yet -- the space is simply left empty rather than showing a white box.
            var logoGo = new GameObject("Logo", typeof(RectTransform), typeof(Image));
            var logoRt = (RectTransform)logoGo.transform;
            logoRt.SetParent(_mainMenuScreen.transform, false);
            logoRt.anchorMin = new Vector2(0.5f, 1f);
            logoRt.anchorMax = new Vector2(0.5f, 1f);
            logoRt.pivot = new Vector2(0.5f, 1f);
            logoRt.anchoredPosition = new Vector2(0f, -24f);
            logoRt.sizeDelta = new Vector2(600f, 212f);   // the art is 1429x503
            var logoImage = logoGo.GetComponent<Image>();
            logoImage.sprite = Resources.Load<Sprite>("Logo/liars_batting_logo");
            logoImage.preserveAspect = true;
            logoImage.raycastTarget = false;
            logoImage.enabled = logoImage.sprite != null;

            // The menu sits below the logo.
            var centered = UiFactory.VerticalGroup(_mainMenuScreen.transform, "Centered", spacing: 14,
                padding: new RectOffset(40, 40, 24, 16));
            centered.anchorMin = new Vector2(0.28f, 0.01f);
            centered.anchorMax = new Vector2(0.72f, 0.62f);
            centered.offsetMin = Vector2.zero;
            centered.offsetMax = Vector2.zero;

            // Greeting row: my profile picture next to "〇〇さん、ようこそ".
            var greetingRow = UiFactory.HorizontalGroup(centered, "Greeting", spacing: 10, childAlign: TextAnchor.MiddleCenter);
            UiFactory.SetHeight(greetingRow, 48);
            UiFactory.SetFlexible(greetingRow, 1, 0);   // a horizontal group would otherwise soak up spare height
            var iconGo = new GameObject("ProfileIcon", typeof(RectTransform), typeof(Image));
            iconGo.transform.SetParent(greetingRow, false);
            UiFactory.SetSize(iconGo.transform, 40, 48);
            _menuProfileIcon = iconGo.GetComponent<Image>();
            _menuProfileIcon.preserveAspect = true;
            _menuProfileIcon.raycastTarget = false;
            _menuProfileIcon.sprite = ProfileScreen.PortraitOf(HeroId.Hunter);
            _menuGreetingText = UiFactory.Text(greetingRow, "", 20, UITheme.Ink, TextAnchor.MiddleLeft, FontStyle.Bold);
            UiFactory.Text(centered, "対戦モードを選択してください", 13, UITheme.Muted, TextAnchor.MiddleCenter);

            var aiBtn = UiFactory.Button(centered, "AIマッチング", UITheme.Accent, Color.white, StartAiMatch, 16);
            UiFactory.SetHeight(aiBtn, 52);
            MenuButtonStyle.Apply(aiBtn, primary: false); // no always-on highlight: the amber border shows on hover only

            var randomBtn = UiFactory.Button(centered, "ランダムマッチ", UITheme.Surface2, UITheme.Ink,
                ShowRandomMatchScreen, 16);
            UiFactory.SetHeight(randomBtn, 52);
            MenuButtonStyle.Apply(randomBtn, primary: false);

            var friendBtn = UiFactory.Button(centered, "フレンドマッチ", UITheme.Surface2, UITheme.Ink,
                ShowFriendMatchScreen, 16);
            UiFactory.SetHeight(friendBtn, 52);
            MenuButtonStyle.Apply(friendBtn, primary: false);

            // Nickname changes now live inside the profile screen.
            var profileBtn = UiFactory.Button(centered, "プロフィール", UITheme.Surface2, UITheme.Ink, ShowProfileScreen, 14);
            UiFactory.SetHeight(profileBtn, 40);
            MenuButtonStyle.Apply(profileBtn, primary: false);

            var settingsBtn = UiFactory.Button(_mainMenuScreen.transform, "設定", UITheme.Surface2, UITheme.Ink, ShowSoundSettings, 14);
            settingsBtn.gameObject.name = "SoundSettingsButton";
            var settingsButtonRect = (RectTransform)settingsBtn.transform;
            settingsButtonRect.anchorMin = new Vector2(1f, 1f);
            settingsButtonRect.anchorMax = new Vector2(1f, 1f);
            settingsButtonRect.pivot = new Vector2(1f, 1f);
            settingsButtonRect.anchoredPosition = new Vector2(-20f, -20f);
            settingsButtonRect.sizeDelta = new Vector2(100f, 40f);
            MenuButtonStyle.Apply(settingsBtn, primary: false);

            // Parented to the root (not the main menu) so the same dialog also works from the
            // header settings button during hero pick, secret pick and the match.
            BuildSoundSettings(root);
        }

        private void BuildSoundSettings(Transform parent)
        {
            var backdrop = UiFactory.FullScreen(parent, "SoundSettings", new Color(0f, 0f, 0f, 0.8f));
            _soundSettings = backdrop.gameObject;
            var centered = UiFactory.HorizontalGroup(backdrop, "SettingsCenter", childAlign: TextAnchor.MiddleCenter);
            centered.anchorMin = new Vector2(0.04f, 0.5f);
            centered.anchorMax = new Vector2(0.96f, 0.5f);
            centered.sizeDelta = new Vector2(0f, 340f);
            centered.anchoredPosition = Vector2.zero;

            var panel = UiFactory.Panel(centered, "SettingsPanel", UITheme.Surface2);
            var panelLayout = panel.gameObject.AddComponent<LayoutElement>();
            panelLayout.preferredWidth = 600f;
            MenuButtonStyle.ApplyPanel(panel.GetComponent<Image>());

            var content = UiFactory.VerticalGroup(panel, "Content", spacing: 12,
                padding: new RectOffset(36, 36, 22, 22));
            UiFactory.StretchToFillParent(content);
            var title = UiFactory.Text(content, "サウンド設定", 24, UITheme.Ink,
                TextAnchor.MiddleCenter, FontStyle.Bold);
            UiFactory.SetHeight(title, 34);
            AddTextShadow(title);
            var divider = UiFactory.Panel(content, "Divider", UITheme.Border);
            UiFactory.SetHeight(divider, 1);
            divider.GetComponent<Image>().raycastTarget = false;

            BuildSoundRow(content, "BGM", "Music", GameAudio.SetMusicLevel,
                GameAudio.SetMusicMuted, out _musicSlider, out _musicMute);
            BuildSoundRow(content, "効果音", "Effects", GameAudio.SetEffectsLevel,
                GameAudio.SetEffectsMuted, out _effectsSlider, out _effectsMute);

            var closeRow = UiFactory.HorizontalGroup(content, "CloseRow", spacing: 0, childAlign: TextAnchor.MiddleCenter);
            UiFactory.SetHeight(closeRow, 50);
            UiFactory.SetFlexible(closeRow, 1, 0);
            var close = UiFactory.Button(closeRow, "閉じる", UITheme.Surface2, UITheme.Ink, CloseSoundSettings, 15);
            close.gameObject.name = "CloseSoundSettingsButton";
            UiFactory.SetSize(close, 180, 48);
            MenuButtonStyle.Apply(close, primary: false);
            _soundSettings.SetActive(false);
        }

        private static void BuildSoundRow(Transform parent, string label, string name,
            Action<float> onVolume, Action<bool> onMute, out Slider slider, out Toggle toggle)
        {
            var section = UiFactory.VerticalGroup(parent, name + "Settings", spacing: 4);
            UiFactory.SetHeight(section, 72);
            UiFactory.SetFlexible(section, 1, 0);   // keep its height when the dialog has spare room
            var heading = UiFactory.Text(section, label, 16, new Color32(214, 160, 70, 255), TextAnchor.MiddleLeft, FontStyle.Bold);
            UiFactory.SetHeight(heading, 24);
            var row = UiFactory.HorizontalGroup(section, "Controls", spacing: 12);
            UiFactory.SetHeight(row, 40);
            UiFactory.SetFlexible(row, 1, 0);

            var sliderObject = new GameObject(name + "Slider", typeof(RectTransform), typeof(Image), typeof(Slider));
            var sliderRect = (RectTransform)sliderObject.transform;
            sliderRect.SetParent(row, false);
            sliderObject.GetComponent<Image>().color = Color.clear;
            UiFactory.SetFlexible(sliderRect);
            slider = sliderObject.GetComponent<Slider>();
            slider.minValue = 0f;
            slider.maxValue = 1f;

            var track = UiFactory.Panel(sliderRect, "Track", UITheme.Border);
            track.anchorMin = new Vector2(0f, 0.5f);
            track.anchorMax = new Vector2(1f, 0.5f);
            track.offsetMin = new Vector2(9f, -3f);
            track.offsetMax = new Vector2(-9f, 3f);
            track.GetComponent<Image>().raycastTarget = false;

            var fillArea = new GameObject("FillArea", typeof(RectTransform)).GetComponent<RectTransform>();
            fillArea.SetParent(sliderRect, false);
            fillArea.anchorMin = new Vector2(0f, 0.5f);
            fillArea.anchorMax = new Vector2(1f, 0.5f);
            fillArea.offsetMin = new Vector2(9f, -3f);
            fillArea.offsetMax = new Vector2(-9f, 3f);
            var fill = UiFactory.Panel(fillArea, "Fill", new Color32(214, 160, 70, 255));   // brass
            fill.anchorMin = Vector2.zero;
            fill.anchorMax = Vector2.one;
            fill.offsetMin = Vector2.zero;
            fill.offsetMax = Vector2.zero;
            fill.GetComponent<Image>().raycastTarget = false;
            slider.fillRect = fill;

            var handleArea = new GameObject("HandleArea", typeof(RectTransform)).GetComponent<RectTransform>();
            handleArea.SetParent(sliderRect, false);
            handleArea.anchorMin = Vector2.zero;
            handleArea.anchorMax = Vector2.one;
            handleArea.offsetMin = new Vector2(9f, 0f);
            handleArea.offsetMax = new Vector2(-9f, 0f);
            var handle = UiFactory.Panel(handleArea, "Handle", UITheme.Ink);
            handle.sizeDelta = new Vector2(18f, 24f);
            slider.handleRect = handle;
            slider.targetGraphic = handle.GetComponent<Image>();

            var percent = UiFactory.Text(row, "", 14, UITheme.Ink, TextAnchor.MiddleRight);
            UiFactory.SetWidth(percent, 46);
            var control = slider;
            slider.onValueChanged.AddListener(value =>
            {
                percent.text = Mathf.RoundToInt(value * 100f) + "%";
                onVolume(value);
            });
            percent.text = Mathf.RoundToInt(control.value * 100f) + "%";

            var toggleObject = new GameObject(name + "Mute", typeof(RectTransform), typeof(Toggle));
            var toggleRect = (RectTransform)toggleObject.transform;
            toggleRect.SetParent(row, false);
            UiFactory.SetWidth(toggleRect, 100);
            toggle = toggleObject.GetComponent<Toggle>();
            var box = UiFactory.Panel(toggleRect, "Box", UITheme.Border);
            box.anchorMin = new Vector2(0f, 0.5f);
            box.anchorMax = new Vector2(0f, 0.5f);
            box.pivot = new Vector2(0f, 0.5f);
            box.sizeDelta = new Vector2(24f, 24f);
            box.anchoredPosition = Vector2.zero;
            var check = UiFactory.Panel(box, "Check", UITheme.Clay);
            check.anchorMin = Vector2.zero;
            check.anchorMax = Vector2.one;
            check.offsetMin = new Vector2(5f, 5f);
            check.offsetMax = new Vector2(-5f, -5f);
            check.GetComponent<Image>().raycastTarget = false;
            toggle.targetGraphic = box.GetComponent<Image>();
            toggle.graphic = check.GetComponent<Image>();
            toggle.onValueChanged.AddListener(value => onMute(value));
            var muteLabel = UiFactory.Text(toggleRect, "ミュート", 14, UITheme.Ink, TextAnchor.MiddleLeft);
            var muteLabelRect = (RectTransform)muteLabel.transform;
            muteLabelRect.anchorMin = Vector2.zero;
            muteLabelRect.anchorMax = Vector2.one;
            muteLabelRect.offsetMin = new Vector2(32f, 0f);
            muteLabelRect.offsetMax = Vector2.zero;
        }

        private void ShowSoundSettings()
        {
            _musicSlider.value = GameAudio.MusicLevel;
            _effectsSlider.value = GameAudio.EffectsLevel;
            _musicMute.isOn = GameAudio.MusicMuted;
            _effectsMute.isOn = GameAudio.EffectsMuted;
            _soundSettings.SetActive(true);
            _soundSettings.transform.SetAsLastSibling(); // above whichever screen is showing
        }

        private void CloseSoundSettings()
        {
            GameAudio.SaveSettings();
            _soundSettings.SetActive(false);
        }

        private void BuildRandomMatchScreen(Transform root)
        {
            _randomMatchScreen = BuildFullScreenContainer(root, "RandomMatchScreen");
            var content = BuildLobbyPanel(_randomMatchScreen.transform, new Vector2(520f, 390f));

            AddLobbyTitle(content, "ランダムマッチ", "同じタイミングでランダムマッチを開始した相手と対戦します。");

            // Four face-down cards; a highlight sweeps across them while searching (AnimateRandomSearch).
            var cardRow = UiFactory.HorizontalGroup(content, "SearchCards", spacing: 12, childAlign: TextAnchor.MiddleCenter);
            UiFactory.SetHeight(cardRow, 78);
            UiFactory.SetFlexible(cardRow, 1, 0);
            for (int i = 0; i < _randomSearchCards.Length; i++)
            {
                var cardGo = new GameObject($"Card{i}", typeof(RectTransform), typeof(Image));
                cardGo.transform.SetParent(cardRow, false);
                UiFactory.SetSize(cardGo.transform, 54, 76);
                var img = cardGo.GetComponent<Image>();
                img.preserveAspect = true;
                img.raycastTarget = false;
                _randomSearchCards[i] = img;
            }

            _randomMatchStatusText = UiFactory.Text(content, "対戦相手を検索中...", 18, UITheme.Ink,
                TextAnchor.MiddleCenter, FontStyle.Bold);
            UiFactory.SetHeight(_randomMatchStatusText, 28);
            AddTextShadow(_randomMatchStatusText);
            _randomMatchWaitText = UiFactory.Text(content, "", 13, UITheme.Muted, TextAnchor.MiddleCenter);
            UiFactory.SetHeight(_randomMatchWaitText, 20);

            var buttonRow = UiFactory.HorizontalGroup(content, "Buttons", spacing: 0, childAlign: TextAnchor.MiddleCenter);
            UiFactory.SetHeight(buttonRow, 52);
            UiFactory.SetFlexible(buttonRow, 1, 0);
            var cancelBtn = UiFactory.Button(buttonRow, "キャンセル", UITheme.Surface2, UITheme.Ink, () =>
            {
                _matchmaking?.LeaveRandomQueue();
                ShowMainMenu();
            }, 16);
            UiFactory.SetSize(cancelBtn, 200, 50);
            MenuButtonStyle.Apply(cancelBtn, primary: false);
        }

        // Same look as the nickname screen: a brass-bordered dark plate in the middle of the screen
        // holding a top-to-bottom column. Returns that column.
        private static RectTransform BuildLobbyPanel(Transform screen, Vector2 size)
        {
            var panel = UiFactory.Panel(screen, "Panel", UITheme.Surface2);
            panel.anchorMin = new Vector2(0.5f, 0.5f);
            panel.anchorMax = new Vector2(0.5f, 0.5f);
            panel.pivot = new Vector2(0.5f, 0.5f);
            panel.sizeDelta = size;
            panel.anchoredPosition = new Vector2(0f, 10f);
            MenuButtonStyle.ApplyPanel(panel.GetComponent<Image>());

            var content = UiFactory.VerticalGroup(panel, "Content", spacing: 12,
                padding: new RectOffset(44, 44, 32, 30), childAlign: TextAnchor.UpperCenter);
            UiFactory.StretchToFillParent(content);
            return content;
        }

        // Shadowed title, a muted one- or two-line description, and a thin divider.
        private static void AddLobbyTitle(Transform content, string title, string description)
        {
            var titleText = UiFactory.Text(content, title, 24, UITheme.Ink, TextAnchor.MiddleCenter, FontStyle.Bold);
            UiFactory.SetHeight(titleText, 34);
            AddTextShadow(titleText);
            var descText = UiFactory.Text(content, description, 13, UITheme.Muted, TextAnchor.MiddleCenter);
            UiFactory.SetHeight(descText, description.Contains("\n") ? 40 : 22);
            var divider = UiFactory.Panel(content, "Divider", UITheme.Border);
            UiFactory.SetHeight(divider, 1);
            divider.GetComponent<Image>().raycastTarget = false;
        }

        // While the random-match screen is up: a highlight walks across the four face-down cards
        // and the wait time counts up.
        private void AnimateRandomSearch()
        {
            float t = Time.unscaledTime - _randomSearchStart;
            int lit = (int)(t * 2.5f) % _randomSearchCards.Length;
            for (int i = 0; i < _randomSearchCards.Length; i++)
            {
                float target = i == lit ? 1f : 0.35f;
                var c = _randomSearchCards[i].color;
                c.a = Mathf.MoveTowards(c.a, target, Time.unscaledDeltaTime * 4f);
                _randomSearchCards[i].color = c;
                _randomSearchCards[i].rectTransform.localScale = Vector3.one * (i == lit ? 1.08f : 1f);
            }
            int s = (int)t;
            _randomMatchWaitText.text = $"待機時間 {s / 60}:{s % 60:00}";
        }

        private void ShowRandomMatchScreen()
        {
            _inviteWatcher?.StopPolling(); // an invite popup must not interrupt the queue (or the match after it)
            ShowOnly(_randomMatchScreen);
            _headerText.text = "ランダムマッチ";
            _tokenText.text = "";
            _randomMatchStatusText.text = "対戦相手を検索中...";
            _randomSearchStart = Time.unscaledTime;
            foreach (var card in _randomSearchCards)
            {
                card.sprite = CardArt.Unknown();   // follows the equipped card skin
                card.color = new Color(CardArt.Tint.r, CardArt.Tint.g, CardArt.Tint.b, 0.35f);
            }

            _matchmaking = new MatchmakingService(_firestore, this, _nickname);
            _matchmaking.JoinRandomQueue(
                (opponent, matchId) =>
                {
                    _matchmaking.LeaveRandomQueue();
                    BeginOnlineMatchFlow(matchId, opponent);
                },
                error => _randomMatchStatusText.text = $"エラー: {error}");
        }

        private void BuildFriendMatchScreen(Transform root)
        {
            _friendMatchScreen = BuildFullScreenContainer(root, "FriendMatchScreen");
            var content = BuildLobbyPanel(_friendMatchScreen.transform, new Vector2(560f, 410f));

            AddLobbyTitle(content, "フレンドマッチ",
                "相手のニックネームを入力して招待を送信してください。\n受信した招待はどの画面にいても自動的に表示されます。");

            var fieldLabel = UiFactory.Text(content, "相手のニックネーム", 13, new Color32(214, 160, 70, 255),
                TextAnchor.MiddleLeft, FontStyle.Bold);
            UiFactory.SetHeight(fieldLabel, 20);

            _friendTargetInput = UiFactory.InputField(content, "例：ライアー");
            _friendTargetInput.GetComponent<Image>().color = new Color(0.04f, 0.035f, 0.03f, 0.9f);   // recessed slot
            var inputOutline = _friendTargetInput.gameObject.AddComponent<Outline>();
            inputOutline.effectColor = UITheme.Border;
            inputOutline.effectDistance = new Vector2(1.5f, -1.5f);
            UiFactory.SetHeight(_friendTargetInput, 50);

            // Fixed height (two lines) so a status message never pushes the buttons around.
            _friendStatusText = UiFactory.Text(content, "", 13, UITheme.Muted, TextAnchor.MiddleCenter);
            UiFactory.SetHeight(_friendStatusText, 36);

            var buttonRow = UiFactory.HorizontalGroup(content, "Buttons", spacing: 12, childAlign: TextAnchor.MiddleCenter);
            UiFactory.SetHeight(buttonRow, 52);
            UiFactory.SetFlexible(buttonRow, 1, 0);

            var backBtn = UiFactory.Button(buttonRow, "メインメニューへ", UITheme.Surface2, UITheme.Ink, () =>
            {
                _matchmaking?.CancelFriendInvite(); // take back an invite nobody has answered yet
                ShowMainMenu();
            }, 15);
            UiFactory.SetSize(backBtn, 170, 50);
            MenuButtonStyle.Apply(backBtn, primary: false);

            _friendInviteButton = UiFactory.Button(buttonRow, "招待を送信", UITheme.Accent, Color.white, () =>
            {
                string target = _friendTargetInput.text.Trim();
                if (target.Length == 0) { ShowFriendStatus("ニックネームを入力してください。", true); return; }
                if (target == _nickname) { ShowFriendStatus("自分自身を招待することはできません。", true); return; }

                _friendInviteButton.interactable = false;
                ShowFriendStatus("招待を送信中... 相手の応答を待っています（最大60秒）", false);

                _matchmaking.SendFriendInvite(target,
                    acceptedMatchId =>
                    {
                        _friendInviteButton.interactable = true;
                        BeginOnlineMatchFlow(acceptedMatchId, target);
                    },
                    () =>
                    {
                        _friendInviteButton.interactable = true;
                        ShowFriendStatus("相手が招待を拒否しました。", true);
                    },
                    error =>
                    {
                        _friendInviteButton.interactable = true;
                        ShowFriendStatus(error, true);
                    });
            }, 16);
            UiFactory.SetSize(_friendInviteButton, 220, 50);
            MenuButtonStyle.Apply(_friendInviteButton, primary: true);
            UiFactory.SubmitOnEnter(_friendTargetInput, _friendInviteButton);
        }

        private void ShowFriendStatus(string message, bool isError)
        {
            _friendStatusText.text = message;
            _friendStatusText.color = isError ? UITheme.Clay : UITheme.Muted;
        }

        private void ShowFriendMatchScreen()
        {
            ShowOnly(_friendMatchScreen);
            _headerText.text = "フレンドマッチ";
            _tokenText.text = "";
            _friendTargetInput.text = "";
            _friendStatusText.text = "";
            // The button is disabled while an invite is pending; leaving the screen mid-wait
            // used to leave it disabled for good.
            _friendInviteButton.interactable = true;

            // Incoming invites are watched globally (see StartGlobalInviteWatch,
            // running since the main menu) -- this instance is only for SENDING.
            _matchmaking?.CancelFriendInvite();
            _matchmaking = new MatchmakingService(_firestore, this, _nickname);
        }

        // Runs continuously from the moment the player reaches the main menu, not
        // just while they happen to be sitting on the friend-match screen -- an
        // invite should pop up no matter where in the app the recipient is.
        private void StartGlobalInviteWatch()
        {
            _inviteWatcher?.StopPolling(); // a fresh instance below gets its own
                                            // _pollRoutine, so the old one must be
                                            // stopped explicitly or both keep running
            _inviteWatcher = new MatchmakingService(_firestore, this, _nickname);
            _inviteWatcher.WatchForIncomingInvite(
                (fromNickname, matchId) =>
                {
                    GameAudio.Play("SFX_Invite_Notification");
                    _choiceOverlay.ShowMixed($"{fromNickname}さんから対戦の申し込みが届きました。",
                        ("承諾", UITheme.Accent, () =>
                        {
                            _inviteWatcher.RespondToInvite(fromNickname, matchId, true,
                                () => BeginOnlineMatchFlow(matchId, fromNickname),
                                error =>
                                {
                                    // Expired/withdrawn invite (or a network error): say so, and go back
                                    // to listening instead of dropping into a match nobody is in.
                                    _choiceOverlay.Show(error, ("確認", () => { }));
                                    StartGlobalInviteWatch();
                                });
                        }),
                        ("拒否", UITheme.Clay, () =>
                        {
                            _inviteWatcher.RespondToInvite(fromNickname, matchId, false,
                                () => { }, error => Debug.LogWarning($"招待の応答に失敗しました: {error}"));
                            StartGlobalInviteWatch(); // resume watching for the next one
                        }));
                },
                error => Debug.LogWarning($"招待の確認に失敗しました: {error}"));
        }

        // Both matchmaking paths land here once paired. One more read of the
        // match doc settles which side (A/B) I am, then the controller takes
        // over turn-by-turn while the existing screens (same CardPickerView,
        // StatusPanelView, ChoiceOverlayView as AI mode) drive the UI -- only
        // the turn-flow methods below source/sink through the network instead
        // of an AiOpponent. Hero selection happens first, same as AI mode.
        private void BeginOnlineMatchFlow(string matchId, string opponentNickname)
        {
            _inviteWatcher?.StopPolling(); // no invite popups in the middle of a match
            _firestore.GetDocument($"matches/{matchId}", doc =>
            {
                if (doc == null)
                {
                    ShowMainMenu();
                    _choiceOverlay.Show("マッチング情報を読み込めませんでした。", ("確認", () => { }));
                    return;
                }

                string playerA = FirestoreClient.ReadString(doc, "playerA");
                var mySide = playerA == _nickname ? MatchSide.A : MatchSide.B;

                // The opponent's equipped title, shown under their portrait once the match screen is up.
                _opponentTitle = "";
                _opponentProgress = null;
                _profiles.Fetch(opponentNickname, other =>
                {
                    _opponentTitle = RewardCatalog.TitleText(other.Progress.Title);
                    _opponentProgress = other.Progress;
                    _opponentHeroPortrait.SetSubtitle(_opponentTitle);
                    if (_matchScreen.activeSelf) _opponentHeroPortrait.SetLook(_state.AiHero, _opponentProgress);
                }, error => Debug.LogWarning($"相手のプロフィール読み込みに失敗しました: {error}"));

                _network = new NetworkMatchController(_firestore, this, matchId, mySide, _nickname, opponentNickname);
                WireNetworkEvents();
                _network.Start();
                _isOnlineMatch = true;
                _matchOver = false;
                _defendingNow = false;
                _awaitingMyChallengeOutcome = false;
                _pendingNotice = null;
                _pendingNoticeDigit = -1;
                _state = new GameState();
                _stats = new MatchStats();
                _nextOnlineTurnExtra = false;

                GameAudio.Play("SFX_Match_Found");
                ShowHeroSelectScreen();
            }, error =>
            {
                ShowMainMenu();
                _choiceOverlay.Show($"マッチング情報を読み込めませんでした: {error}", ("確認", () => { }));
            });
        }

        private void WireNetworkEvents()
        {
            _network.OnBothHeroesReady += opponentHero =>
            {
                _state.AiHero = opponentHero;
                ProceedToSecretSetup();
            };

            _network.OnBothSecretsReady += () =>
            {
                _surrenderButton.gameObject.SetActive(true);
                RefreshTokenHeader();
                // Player A always moves first (the controller's turn starts on A).
                bool iAmFirst = _network.CurrentAttacker == _network.MySide;
                ShowFirstTurnBanner(iAmFirst, UpdateOnlineTurnUI);
            };

            _network.OnOpponentGuess += guess =>
            {
                if (_matchOver) return;
                var trueResult = Judge.Evaluate(_state.PlayerSecret, guess);
                bool canLieAboutWin = _state.PlayerHero == HeroId.Hunter;
                _defendingNow = true;
                PromptDefenseChoice(guess, trueResult, _state.PlayerLieTokens, canLieAboutWin, (reported, usedToken) =>
                {
                    _defendingNow = false;
                    if (usedToken) { _state.PlayerLieTokens--; RefreshTokenHeader(); }
                    bool wasLie = !reported.Equals(trueResult);
                    _statusPanel.OpponentAttackHistory.AddRow(guess, reported, wasLie);
                    // A reported win ends the match right here: the controller fires OnGameOver for me too,
                    // since my own "finished" write never comes back through my own poll.
                    _network.SubmitDefenseResponse(trueResult, reported);
                    if (_matchOver) return;
                    _headerText.text = $"{_network.OpponentNickname}さんの判断を待機中...";
                    _attackPicker.SetInteractable(false);
                    _timer.StartSpectating(TrustChoiceSeconds);   // the opponent's trust-or-challenge clock
                });
            };

            _network.OnOpponentResponse += reported =>
            {
                if (_matchOver) return;
                _statusPanel.MyAttackHistory.AddRow(_myLastOnlineGuess, reported, false, revealLie: false);
                PromptTrustOrChallenge(reported, null,
                    onTrust: () =>
                    {
                        _network.SubmitTrust();
                        UpdateOnlineTurnUI();
                    },
                    onChallenge: () =>
                    {
                        _stats.Challenges++;
                        _awaitingMyChallengeOutcome = true;
                        _network.SubmitChallenge();
                        _headerText.text = $"{_network.OpponentNickname}さんの応答を待機中...";
                    });
            };

            _network.OnChallengeNeedsMyJudgement += () =>
            {
                if (_matchOver) return;
                _timer.Stop();   // the opponent decided: its clock on my screen is done
                bool wasLie = _network.DidIActuallyLie();
                GameAudio.Play("SFX_Challenge_Doubt");
                GameAudio.Play(wasLie ? "SFX_Reveal_Punished" : "SFX_Reveal_Busted");
                _network.ResolveChallenge(wasLie);
            };

            _network.OnIMustReveal += () =>
            {
                if (_matchOver) return;
                _timer.Stop();
                string message;
                if (_awaitingMyChallengeOutcome)
                {
                    // I challenged but the opponent told the truth -- I'm the one who has to open a digit.
                    _awaitingMyChallengeOutcome = false;
                    _stats.ChallengesWrong++;
                    _statusPanel.MyAttackHistory.MarkLastRowAsTruth();
                    GameAudio.Play("SFX_Reveal_Punished");
                    message = "空振り！ 相手は真実を告げていました。\nペナルティとして自分の暗証番号を1桁公開してください。";
                }
                else
                {
                    // The opponent challenged and I had lied: caught.
                    _stats.LiesCaught++;
                    message = "相手があなたの嘘を見破りました！\nペナルティとして自分の暗証番号を1桁公開してください。";
                }
                PromptPlayerRevealChoice(message, index =>
                {
                    if (index < 0) { UpdateOnlineTurnUI(); return; }
                    _network.SubmitReveal(index, _state.PlayerSecret[index]);
                    if (AllRevealed(_state.PlayerRevealed))
                    {
                        // That was my last hidden digit: the opponent now knows my whole secret.
                        _network.SubmitGameOver(_network.OpponentSide, NetworkMatchController.GameOverByFullReveal);
                        return;
                    }
                    UpdateOnlineTurnUI();
                });
            };

            _network.OnOpponentRevealed += (side, index, digit) =>
            {
                if (_matchOver || index < 0 || index > 3) return;
                _state.AiSecret[index] = digit;
                _state.AiRevealed[index] = true;
                RefreshRevealedRows();

                bool iChallenged = _awaitingMyChallengeOutcome;
                _awaitingMyChallengeOutcome = false;
                if (iChallenged)
                {
                    _stats.ChallengesCorrect++;
                    // I challenged and the opponent now has to open a digit: their answer was a lie.
                    _statusPanel.MyAttackHistory.MarkLastRowAsLie();
                    GameAudio.Play("SFX_Reveal_Busted");
                }

                if (AllRevealed(_state.AiRevealed))
                {
                    EndOnlineGame(true, "相手の暗証番号がすべて公開された");
                    return;
                }

                // The turn change arrives in the same poll; the banner is shown ahead of it.
                QueueNotice(iChallenged
                        ? RevealNoticeText("的中！", "相手は嘘をついていました", index)
                        : RevealNoticeText("相手の空振り！", "相手があなたを疑いましたが、外れました", index),
                    RevealNoticeHoldSeconds, digit);
            };

            _network.OnOpponentTrusted += () =>
            {
                if (_matchOver) return;
                if (_lastDefenseWasLie) _stats.LieBelieved();
                GameAudio.Play("SFX_Trust_Accept");
                QueueNotice("<size=48>相手はあなたの答えを信じました</size>", NoticeHoldSeconds);
            };

            _network.OnOpponentWizardReveal += (index, digit) =>
            {
                if (_matchOver || index < 0 || index > 3) return;
                _state.AiSecret[index] = digit;
                _state.AiRevealed[index] = true;
                RefreshRevealedRows();
                if (AllRevealed(_state.AiRevealed)) EndOnlineGame(true, "相手の暗証番号がすべて公開された");
            };

            _network.OnTurnChanged += _ => UpdateOnlineTurnUI();

            _network.OnGameOver += (winner, reason) =>
            {
                bool won = winner == _network.MySide;
                if (won && reason == NetworkMatchController.GameOverByGuess && !_matchOver)
                {
                    _stats.WonByGuess = true;
                    _stats.WonOnExtraTurn = _stats.OnExtraTurn;
                }
                string message =
                    reason == NetworkMatchController.GameOverByFullReveal
                        ? (won ? "相手の暗証番号がすべて公開された" : "自分の暗証番号がすべて公開された")
                    : reason == NetworkMatchController.GameOverByDisconnect
                        ? (won ? "相手の接続が切断された" : "接続が切断され敗北した")
                    : null;
                EndOnlineGame(won, message);
            };
            _network.OnOpponentSurrendered += () => EndOnlineGame(true, "相手が降伏しました");

            // The opponent closed the game or lost its connection: record it as my win (the
            // "finished" write also tells them they lost if they come back).
            _network.OnOpponentDisconnected += () =>
            {
                if (_matchOver) return;
                _network.SubmitGameOver(_network.MySide, NetworkMatchController.GameOverByDisconnect);
            };

            _network.OnError += error => ShowFriendStatus(error, true); // surfaces even off the friend screen; harmless no-op UI otherwise

            // Rogue: opponent (as attacker) asked whether I actually lied on my
            // last defense -- NetworkMatchController already auto-answers this
            // truthfully from _myTrueResultForCurrentDefense, no UI needed here.
            // OnRogueAnswer is consumed one-shot from inside PromptTrustOrChallenge
            // itself when I'M the one spending the charge, so nothing to wire here.

            // Priest: opponent (as attacker) is asking whether one specific
            // position holds one specific digit, truthfully or not.
            _network.OnOpponentPriestQuery += (position, guessedDigit) =>
            {
                if (_matchOver) return;
                _timer.Stop();   // the opponent is done guessing; their clock on my screen is moot
                PromptPriestDefenseChoice(position, guessedDigit, answer =>
                {
                    _network.SubmitPriestAnswer(answer);
                    // My own write's echo is filtered out of my own poll (see
                    // NetworkMatchController's seq-gate), so nothing will ever
                    // fire OnTurnChanged for ME here -- I have to drive my own
                    // UI into the new turn state myself.
                    UpdateOnlineTurnUI();
                });
            };

            _network.OnPriestAnswer += answer =>
            {
                if (answer) _stats.PriestYes++;
                string text = answer ? "その通りです" : "違います";
                _choiceOverlay.Show($"相手の回答: \"{text}\"", ("確認", () => { }));
            };

            // Wizard: opponent used their ability, so I must reveal one of my own
            // still-hidden digits back to them.
            _network.OnMustRevealForWizard += () =>
            {
                if (_matchOver) return;
                int idx = PickRandomOpenIndex(_state.PlayerRevealed);
                if (idx < 0) return;
                _state.PlayerRevealed[idx] = true;
                RefreshRevealedRows();
                _network.SubmitWizardResponse(idx, _state.PlayerSecret[idx]);
                // Their ability just opened my last hidden digit: my whole secret is out.
                if (AllRevealed(_state.PlayerRevealed))
                    _network.SubmitGameOver(_network.OpponentSide, NetworkMatchController.GameOverByFullReveal);
            };
        }

        private void UpdateOnlineTurnUI()
        {
            if (_matchOver) return;
            _timer.Stop();

            // A notice queued by this poll's trust / reveal event goes first; the turn banner follows.
            if (_pendingNotice != null)
            {
                string notice = _pendingNotice;
                float hold = _pendingNoticeHold;
                int digit = _pendingNoticeDigit;
                _pendingNotice = null;
                _pendingNoticeDigit = -1;
                ShowBanner(notice, hold, UpdateOnlineTurnUI, digit);
                return;
            }

            if (_network.CurrentAttacker == _network.MySide) BeginOnlineAttackTurn();
            else BeginOnlineWaitTurn();
        }

        private void BeginOnlineAttackTurn()
        {
            GameAudio.Music("BGM_Match_Tension");
            _timer.Stop();
            _attackPicker.SetInteractable(false);
            ShowTurnAnnouncement("自分のターン", BeginOnlineAttackTurnAfterAnnouncement);
        }

        private void BeginOnlineAttackTurnAfterAnnouncement()
        {
            _stats.OnExtraTurn = _nextOnlineTurnExtra;
            _nextOnlineTurnExtra = false;
            RefreshTokenHeader();
            _headerText.text = $"自分のターン — {_network.OpponentNickname}さんの暗証番号を推測してください";
            _attackPicker.SetInteractable(true);
            SetMyTurnActive(true);
            _timer.Start(GuessSeconds, () =>
            {
                // e.g. the Priest position/digit picker may still be open.
                _choiceOverlay.Hide();
                _attackPicker.SetInteractable(false);
                var randomGuess = RandomDigits(false);
                ShowTimeoutNotice($"ランダムな数字 {string.Join(" ", randomGuess)} で推測します",
                    () => OnOnlineGuessSubmitted(randomGuess));
            });
        }

        private void BeginOnlineWaitTurn()
        {
            GameAudio.Music("BGM_Match_Tension");
            _timer.Stop();
            _attackPicker.SetInteractable(false);
            ShowTurnAnnouncement("相手のターン", BeginOnlineWaitTurnAfterAnnouncement);
        }

        private void BeginOnlineWaitTurnAfterAnnouncement()
        {
            RefreshTokenHeader();
            _attackPicker.SetInteractable(false);
            SetMyTurnActive(false);
            // The opponent's guess can land while this banner is still up: don't disturb the clock of
            // the answer I'm already giving.
            if (_defendingNow) return;
            _headerText.text = $"{_network.OpponentNickname}さんのターン — 待機中";
            _timer.StartSpectating(GuessSeconds);   // the opponent's guessing clock, so I can see it run
        }

        private void OnOnlineGuessSubmitted(int[] guess)
        {
            _timer.Stop();
            _attackPicker.ResetAll();
            _myLastOnlineGuess = guess;
            _attackPicker.SetInteractable(false);
            _headerText.text = $"{_network.OpponentNickname}さんの応答を待機中...";
            bool keepTurn = _state.PlayerExtraTurnPending;
            _nextOnlineTurnExtra = keepTurn;   // my next attack turn is a kept (Warrior) turn
            _state.PlayerExtraTurnPending = false;
            _network.SubmitGuess(guess, keepTurn);
            _timer.StartSpectating(DefendChoiceSeconds);   // the opponent's truth-or-lie clock
        }

        private void EndOnlineGame(bool won, string message = null)
        {
            if (_matchOver) return;   // the loser's own write, the winner's poll and a local check can all report it
            _matchOver = true;
            _timer.Stop();
            _network?.Stop();
            _matchSession++;
            CancelTurnAnnouncement();
            _surrenderButton.gameObject.SetActive(false);
            GameAudio.Music(won ? "BGM_Win_Stinger" : "BGM_Lose_Stinger", false);
            // A win by guessing leaves the opponent's secret unknown here, so only show its cards when
            // every digit has been opened; a defeat always shows my own secret.
            int[] cards = won ? (AllRevealed(_state.AiRevealed) ? _state.AiSecret : null) : _state.PlayerSecret;
            _resultPopup.Show(won,
                message ?? (won ? "相手の暗証番号を当てた" : "自分の暗証番号が見破られた"),
                cards);
            SettleMatch(won);
        }

        // XP, level, challenges and (online) the record against people -- once per match, after the
        // result popup is up so its summary line can be filled in when the numbers are ready.
        private void SettleMatch(bool won)
        {
            var stats = _stats;
            stats.Hero = _state.PlayerHero;
            stats.Online = _isOnlineMatch;
            stats.Won = won;
            stats.MyDigitsRevealed = 4 - HiddenCount(_state.PlayerRevealed);
            stats.OpponentDigitsRevealed = 4 - HiddenCount(_state.AiRevealed);
            stats.DurationSeconds = StopMatchClock();
            _resultPopup.SetSummary("経験値計算中...");
            _profiles.SettleMatch(_nickname, stats, settlement =>
            {
                // null = an early surrender: the result still counts, but nothing is earned.
                _resultPopup.SetSummary(settlement != null
                    ? settlement.ToSummary()
                    : $"{LevelTable.MinSurrenderSecondsForXp / 60}分経過前に降伏したため、経験値を獲得できませんでした。");
                ApplyCosmetics(_profiles.Current);   // a new level may have changed nothing equipped, but keep it in sync
            });
        }

        // ---------- hero select ----------

        private void BuildHeroSelectScreen(Transform root)
        {
            _heroSelectScreen = BuildFullScreenContainer(root, "HeroSelectScreen");

            // Brass-bordered plate like the other lobby screens, nudged down so the big countdown
            // number under the header stays clear of it.
            var panel = UiFactory.Panel(_heroSelectScreen.transform, "Panel", UITheme.Surface2);
            panel.anchorMin = new Vector2(0.5f, 0.5f);
            panel.anchorMax = new Vector2(0.5f, 0.5f);
            panel.pivot = new Vector2(0.5f, 0.5f);
            panel.sizeDelta = new Vector2(800f, 532f);
            panel.anchoredPosition = new Vector2(0f, -30f);
            MenuButtonStyle.ApplyPanel(panel.GetComponent<Image>());
            var centered = UiFactory.VerticalGroup(panel, "Content", spacing: 10,
                padding: new RectOffset(36, 36, 22, 20), childAlign: TextAnchor.UpperCenter);
            UiFactory.StretchToFillParent(centered);

            var title = UiFactory.Text(centered, "ヒーローを選択してください", 24, UITheme.Ink, TextAnchor.MiddleCenter, FontStyle.Bold);
            UiFactory.SetHeight(title, 34);
            AddTextShadow(title);

            var grid = UiFactory.Grid(centered, "HeroGrid", columns: 4, cellSize: 170, spacing: 14);
            var gridLayout = grid.GetComponent<GridLayoutGroup>();
            gridLayout.cellSize = new Vector2(170, 214);
            gridLayout.childAlignment = TextAnchor.UpperCenter;      // keep the cards centred on screen
            UiFactory.SetHeight(grid, 214 * 2 + 14);                 // 7 heroes over 4 columns = 2 rows

            var brass = new Color32(214, 160, 70, 255);
            foreach (var info in HeroCatalog.All)
            {
                var heroId = info.Id;
                var cardGo = new GameObject($"Hero_{heroId}", typeof(RectTransform), typeof(Image), typeof(Button));
                cardGo.transform.SetParent(grid, false);
                var img = cardGo.GetComponent<Image>();
                img.color = UITheme.Surface2;                        // fallback if the plate sprites are missing
                var btn = cardGo.GetComponent<Button>();
                btn.targetGraphic = img;
                cardGo.AddComponent<ButtonHoverSound>();
                btn.onClick.AddListener(() => GameAudio.Play("SFX_UI_Click", volume: 0.6f));
                btn.onClick.AddListener(() => OnHeroChosen(heroId));
                _heroSelectButtons.Add(btn);

                var inner = UiFactory.VerticalGroup(cardGo.transform, "Inner", spacing: 4,
                    padding: new RectOffset(10, 10, 14, 8), childAlign: TextAnchor.UpperCenter);
                UiFactory.StretchToFillParent(inner);

                var portraitGo = new GameObject("Portrait", typeof(RectTransform), typeof(Image));
                portraitGo.transform.SetParent(inner, false);
                UiFactory.SetSize(portraitGo.transform, 100, 100);
                var portraitImg = portraitGo.GetComponent<Image>();
                portraitImg.sprite = Resources.Load<Sprite>($"Heroes/Hero_{heroId}_Portrait");
                portraitImg.preserveAspect = true;
                portraitImg.raycastTarget = false;
                _heroSelectPortraits[heroId] = portraitImg;

                // My mastery level with this hero, top-right of the card (filled in ShowHeroSelectScreen).
                var levelText = UiFactory.Text(cardGo.transform, "", 13, brass, TextAnchor.UpperRight, FontStyle.Bold);
                levelText.raycastTarget = false;
                var levelRt = (RectTransform)levelText.transform;
                levelRt.anchorMin = new Vector2(1, 1);
                levelRt.anchorMax = new Vector2(1, 1);
                levelRt.pivot = new Vector2(1, 1);
                levelRt.anchoredPosition = new Vector2(-12, -8);
                levelRt.sizeDelta = new Vector2(80, 20);
                _heroSelectLevels[heroId] = levelText;

                var nameText = UiFactory.Text(inner, info.Name, 17, UITheme.Ink, TextAnchor.MiddleCenter, FontStyle.Bold);
                nameText.raycastTarget = false;
                var abilityText = UiFactory.Text(inner, info.AbilityName, 12, brass, TextAnchor.MiddleCenter, FontStyle.Bold);
                abilityText.raycastTarget = false;
                var descText = UiFactory.Text(inner, info.AbilityDescription, 11, UITheme.Muted, TextAnchor.UpperCenter);
                descText.raycastTarget = false;

                MenuButtonStyle.ApplyChoice(btn, UITheme.Accent);    // same charcoal plate / brass hover as the choice popups
            }
        }

        private void SetHeroButtonsInteractable(bool interactable)
        {
            foreach (var b in _heroSelectButtons) b.interactable = interactable;
        }

        private void ShowHeroSelectScreen()
        {
            ShowOnly(_heroSelectScreen);
            SetHeroButtonsInteractable(true);
            var progress = _profiles.Current?.Progress;
            foreach (var pair in _heroSelectLevels)
                pair.Value.text = progress != null ? $"Lv.{progress.HeroLevel(pair.Key)}" : "";
            foreach (var pair in _heroSelectPortraits)
                PortraitLook.Apply(pair.Value, pair.Key, progress);
            _headerText.text = _isOnlineMatch ? $"ヒーロー選択 — 相手: {_network.OpponentNickname}" : "ヒーローを選択してください";
            _tokenText.text = "";
            _timer.Start(HeroPickSeconds, () =>
            {
                // Same as the other timeouts: say what was picked before moving on.
                SetHeroButtonsInteractable(false);
                var hero = RandomHeroId();
                ShowTimeoutNotice($"ランダムで「{HeroCatalog.Get(hero).Name}」が選択されました",
                    () => OnHeroChosen(hero));
            });
        }

        private void OnHeroChosen(HeroId hero)
        {
            _timer.Stop();
            SetHeroButtonsInteractable(false);

            _state.PlayerHero = hero;
            var info = HeroCatalog.Get(hero);
            _state.PlayerAbilityCharges = Mathf.Max(0, info.Charges);

            if (_isOnlineMatch)
            {
                _headerText.text = "相手のヒーロー選択を待機中...";
                _network.MarkMyHeroReady(hero);
                return; // ProceedToSecretSetup() fires from OnBothHeroesReady
            }

            ProceedToSecretSetup();
        }

        private void ProceedToSecretSetup()
        {
            if (!_isOnlineMatch)
            {
                // DemonHunter restricts the OPPONENT's pool -- if the PLAYER picked
                // DemonHunter, it's the AI's own secret (not its guessing target)
                // that must avoid 9 here.
                bool exclude9 = _state.PlayerHero == HeroId.DemonHunter;
                _state.AiSecret = _ai.PickRandomSecret(exclude9);
            }
            ShowSetupScreen();
        }

        // ---------- setup (secret pick) ----------

        private static HeroId RandomHeroId() => (HeroId)UnityEngine.Random.Range(0, HeroCatalog.All.Length);

        private static int[] RandomDigits(bool exclude9)
        {
            var pool = new List<int>();
            for (int d = 0; d <= 9; d++)
                if (!exclude9 || d != 9) pool.Add(d);
            var result = new int[4];
            for (int i = 0; i < 4; i++)
            {
                int idx = UnityEngine.Random.Range(0, pool.Count);
                result[i] = pool[idx];
                pool.RemoveAt(idx);
            }
            return result;
        }

        private void BuildSetupScreen(Transform root)
        {
            _setupScreen = new GameObject("SetupScreen", typeof(RectTransform));
            var rt = (RectTransform)_setupScreen.transform;
            rt.SetParent(root, false);
            rt.anchorMin = Vector2.zero;
            rt.anchorMax = Vector2.one;
            rt.offsetMin = new Vector2(0, 0);
            rt.offsetMax = new Vector2(0, -56);

            // Brass-bordered plate like the other lobby screens, nudged down so the big countdown
            // number under the header stays clear of it.
            var panel = UiFactory.Panel(rt, "Panel", UITheme.Surface2);
            panel.anchorMin = new Vector2(0.5f, 0.5f);
            panel.anchorMax = new Vector2(0.5f, 0.5f);
            panel.pivot = new Vector2(0.5f, 0.5f);
            panel.sizeDelta = new Vector2(660f, 540f);
            panel.anchoredPosition = new Vector2(0f, -30f);
            MenuButtonStyle.ApplyPanel(panel.GetComponent<Image>());
            var centered = UiFactory.VerticalGroup(panel, "Content", spacing: 10,
                padding: new RectOffset(40, 40, 22, 20), childAlign: TextAnchor.UpperCenter);
            UiFactory.StretchToFillParent(centered);

            AddLobbyTitle(centered, "暗証番号4桁を決めてください", "0〜9の重複しない数字4個。相手はこの番号を見ることができません。");

            _setupPicker = new CardPickerView(centered, "自分の暗証番号", "ゲーム開始", OnSecretChosen);
        }

        // Shared by both modes: at this point _state already has both heroes set
        // (AI mode set AiHero at StartAiMatch; online mode set it in
        // OnBothHeroesReady) so the DemonHunter restriction can be applied here
        // regardless of which mode this match is.
        private void ShowSetupScreen()
        {
            ShowOnly(_setupScreen);
            bool opponentIsDemonHunter = _state.AiHero == HeroId.DemonHunter;
            _setupPicker.SetInteractable(true);
            _setupPicker.SetDigitEnabled(9, !opponentIsDemonHunter);

            _headerText.text = opponentIsDemonHunter
                ? "暗証番号の準備 — 相手の能力により9は使用できません"
                : "暗証番号の準備";
            _tokenText.text = "";

            _timer.Start(SecretPickSeconds, () =>
            {
                _setupPicker.SetInteractable(false);
                var randomSecret = RandomDigits(opponentIsDemonHunter);
                ShowTimeoutNotice($"自分の暗証番号が {string.Join(" ", randomSecret)} に設定されました",
                    () => OnSecretChosen(randomSecret));
            });
        }

        private void BuildMatchScreen(Transform root)
        {
            _matchScreen = new GameObject("MatchScreen", typeof(RectTransform));
            var rt = (RectTransform)_matchScreen.transform;
            rt.SetParent(root, false);
            rt.anchorMin = Vector2.zero;
            rt.anchorMax = Vector2.one;
            rt.offsetMin = Vector2.zero;
            rt.offsetMax = new Vector2(0, -56);

            // Everything sits on a fixed-size stage (see MatchHudView for the layout).
            _statusPanel = new MatchHudView(rt, this);
            var stage = _statusPanel.Stage;

            _opponentHeroPortrait = new HeroPortraitView(stage, isMine: false);
            _statusPanel.PlaceOpponentHero(_opponentHeroPortrait.Root);

            _myHeroPortrait = new HeroPortraitView(stage, isMine: true);
            _statusPanel.PlaceMyHero(_myHeroPortrait.Root);

            _attackPicker = new CardPickerView(_statusPanel.GuessSlotsHolder, _statusPanel.TrayHolder,
                "推測提出", OnPlayerGuessSubmitted, new Vector2(76, 106), new Vector2(64, 86));

            // The hourglass shows the countdown during a match (the header badge is hidden).
            // The choice popups cover the hourglass, so they mirror the remaining seconds themselves.
            _timer.OnTick = (remaining, total) =>
            {
                _statusPanel.Hourglass.SetRemaining(remaining, total);
                // Only MY decisions get the popup countdown; the opponent's clock is hourglass-only.
                if (!_timer.IsSpectating) _choiceOverlay?.SetCountdown(remaining);
            };
            _timer.OnStopped = () =>
            {
                _statusPanel.Hourglass.Idle();
                _choiceOverlay?.HideCountdown();
            };

            _matchScreen.SetActive(false);
        }

        // The "single click" active abilities. Rogue is used from the trust/challenge
        // popup and Priest opens its own position picker -- see RefreshMyAbilityUI,
        // which decides what the portrait's ability button does for each hero.
        private void OnMyAbilityClicked()
        {
            if (_state == null || _state.PlayerAbilityCharges <= 0) return;
            switch (_state.PlayerHero)
            {
                case HeroId.Paladin: UsePaladinHeal(); break;
                case HeroId.Warrior: UseWarriorAbility(); break;
                case HeroId.Wizard: UseWizardAbility(); break;
            }
        }

        private void UsePaladinHeal()
        {
            // Healing at full LIE TOKENs would just waste the charge (the button is
            // disabled then too -- see RefreshMyAbilityUI).
            if (_state.PlayerLieTokens >= GameState.MaxLieTokens) return;
            GameAudio.Play("SFX_Hero_Paladin_Restore");
            _stats.AbilityUses++;
            _stats.PaladinHealed = true;
            _state.PlayerAbilityCharges--;
            _state.PlayerLieTokens = Mathf.Min(_state.PlayerLieTokens + 1, GameState.MaxLieTokens);
            RefreshTokenHeader();
            RefreshMyAbilityUI();
        }

        // Arms a flag consumed the next time I actually end my attack turn (see
        // EndMyAttackTurnAndPassToOpponent / the online SubmitGuess keepTurn
        // param) -- clicking it isn't itself turn-scoped, so it's safe to press
        // any time and it just pre-arms my next attack.
        private void UseWarriorAbility()
        {
            GameAudio.Play("SFX_Hero_Warrior_Rush");
            _stats.AbilityUses++;
            _state.PlayerAbilityCharges--;
            _state.PlayerExtraTurnPending = true;
            RefreshMyAbilityUI();
        }

        private void UseWizardAbility()
        {
            // Opening my own LAST hidden digit would lose me the match (every digit open = the other side
            // knows my whole secret), so this is blocked -- the button is disabled in that state as well.
            if (HiddenCount(_state.PlayerRevealed) <= 1) return;
            GameAudio.Play("SFX_Hero_Wizard_Clairvoyance");
            _stats.AbilityUses++;
            _stats.GuessesAtWizard = _stats.Guesses;
            _state.PlayerAbilityCharges--;
            int myIdx = PickRandomOpenIndex(_state.PlayerRevealed);
            if (myIdx >= 0)
            {
                _state.PlayerRevealed[myIdx] = true;
                if (_isOnlineMatch)
                {
                    _network.SubmitWizardUse(myIdx, _state.PlayerSecret[myIdx]);
                }
                else
                {
                    int oppIdx = PickRandomOpenIndex(_state.AiRevealed);
                    if (oppIdx >= 0) _state.AiRevealed[oppIdx] = true;
                }
            }
            RefreshRevealedRows();
            RefreshMyAbilityUI();
            if (!_isOnlineMatch) EndAiMatchIfAiFullyRevealed();   // the ability opened the AI's last hidden digit
        }

        // The ability button under my portrait: active heroes get "能力使用 (残/全)",
        // passive heroes get "パッシブ説明" (nothing to use -- the button just explains it).
        private void RefreshMyAbilityUI()
        {
            if (_state == null) return;
            var info = HeroCatalog.Get(_state.PlayerHero);

            if (info.Charges <= 0)
            {
                _myHeroPortrait.SetAbilityButton("パッシブ説明", true, ShowMyAbilityDescription);
                return;
            }

            int charges = _state.PlayerAbilityCharges;
            if (_state.PlayerHero == HeroId.Warrior && _state.PlayerExtraTurnPending)
            {
                _myHeroPortrait.SetAbilityButton("ターン維持予約済み", false, null);
                return;
            }
            if (charges <= 0)
            {
                _myHeroPortrait.SetAbilityButton("使用済み", false, null);
                return;
            }

            string label = $"能力使用 {charges}/{info.Charges}";
            switch (_state.PlayerHero)
            {
                case HeroId.Priest:
                    // A Priest query replaces a normal guess, so it's only usable on my own attack turn.
                    _myHeroPortrait.SetAbilityButton(label, _myTurnActive, ShowPriestPositionPicker);
                    break;
                case HeroId.Rogue:
                    _myHeroPortrait.SetAbilityButton(label, true, ShowRogueUsageHint);
                    break;
                case HeroId.Paladin:
                    // Nothing to restore while both LIE TOKENs are still there.
                    if (_state.PlayerLieTokens >= GameState.MaxLieTokens)
                        _myHeroPortrait.SetAbilityButton("トークン満杯", false, null);
                    else
                        _myHeroPortrait.SetAbilityButton(label, true, OnMyAbilityClicked);
                    break;
                case HeroId.Wizard:
                    // The ability opens one of MY hidden digits too: not when only one is left.
                    if (HiddenCount(_state.PlayerRevealed) <= 1)
                        _myHeroPortrait.SetAbilityButton("公開可能な桁不足", false, null);
                    else
                        _myHeroPortrait.SetAbilityButton(label, true, OnMyAbilityClicked);
                    break;
                default:
                    _myHeroPortrait.SetAbilityButton(label, true, OnMyAbilityClicked);
                    break;
            }
        }

        private void ShowMyAbilityDescription()
        {
            var info = HeroCatalog.Get(_state.PlayerHero);
            _choiceOverlay.Show($"{info.AbilityName} (パッシブ)\n{info.AbilityDescription}\n\n個別に使用しなくても自動的に適用されます。",
                ("確認", () => { }));
        }

        // Rogue's ability needs a response to judge, so it can't be fired from here.
        private void ShowRogueUsageHint()
        {
            _choiceOverlay.Show("真実看破は相手の応答を判断する選択ウィンドウで\n「真実看破」ボタンから使用します。", ("確認", () => { }));
        }

        private static int PickRandomOpenIndex(bool[] revealed)
        {
            var open = new List<int>();
            for (int i = 0; i < revealed.Length; i++)
                if (!revealed[i]) open.Add(i);
            return open.Count > 0 ? open[UnityEngine.Random.Range(0, open.Count)] : -1;
        }

        // Tracks whether it's my attack turn right now, which is what gates the Priest
        // button (it's an ALTERNATIVE to submitting a normal guess this turn).
        private void SetMyTurnActive(bool active)
        {
            _myTurnActive = active;
            RefreshMyAbilityUI();
        }

        private void ShowPriestPositionPicker()
        {
            var buttons = new (string, Action)[4];
            for (int i = 0; i < 4; i++)
            {
                int pos = i;
                buttons[i] = ($"{pos + 1}桁目", () => ShowPriestDigitGuessPicker(pos));
            }
            _choiceOverlay.Show("どの桁について尋ねますか？", buttons);
        }

        // Second step: guess a specific digit for that position ("2桁目
        // の数字は5？") instead of asking an open "what's the digit" question.
        private void ShowPriestDigitGuessPicker(int position)
        {
            var options = new List<(string, Action)>();
            for (int d = 0; d <= 9; d++)
            {
                int digit = d;
                options.Add((digit.ToString(), () => OnPriestQuerySubmitted(position, digit)));
            }
            _choiceOverlay.Show($"{position + 1}桁目、どの数字か尋ねますか？", options.ToArray());
        }

        private void OnPriestQuerySubmitted(int position, int guessedDigit)
        {
            GameAudio.Play("SFX_Hero_Priest_Inquiry");
            _stats.AbilityUses++;
            _timer.Stop();
            _state.PlayerAbilityCharges--;
            RefreshMyAbilityUI();
            SetMyTurnActive(false);
            _attackPicker.SetInteractable(false);
            _attackPicker.ResetAll();

            if (_isOnlineMatch)
            {
                _headerText.text = $"{_network.OpponentNickname}さんの回答を待機中...";
                _network.SubmitPriestQuery(position, guessedDigit);
            }
            else
            {
                bool answer = _state.AiSecret[position] == guessedDigit;
                if (answer) _stats.PriestYes++;
                string answerText = answer ? "その通りです" : "違います";
                _choiceOverlay.Show($"相手の回答: {position + 1}桁目が {guessedDigit} かどうかについて \"{answerText}\"",
                    ("確認", EndMyAttackTurnAndPassToOpponent));
            }
        }

        // Shared by AI mode (AI asks, I answer) and online mode (opponent asks,
        // I answer). Priest's query always gets a truthful answer -- no LIE
        // TOKEN option here, unlike a normal guess's strike/ball report.
        private void PromptPriestDefenseChoice(int position, int guessedDigit, Action<bool> onAnswered)
        {
            bool trueAnswer = _state.PlayerSecret[position] == guessedDigit;
            string truthLabel = trueAnswer ? "はい" : "いいえ";
            string message = $"相手からの質問: {position + 1}桁目は {guessedDigit} ですか？\n実際の答え: {truthLabel}";
            _choiceOverlay.Show(message, ("確認", () => onAnswered(trueAnswer)));
        }

        private void BuildGameOverScreen(Transform root)
        {
            _resultPopup = new ResultPopupView(root, this, () =>
            {
                _timer.Stop();
                _network?.Stop();
                _network = null;
                _isOnlineMatch = false;
                _gameOverScreen.SetActive(false);
                ShowMainMenu();
            });
            _gameOverScreen = _resultPopup.Root.gameObject;
            _gameOverScreen.SetActive(false);
        }

        // ---------- flow ----------

        // The top-level screens are mutually exclusive; overlays (game over, the
        // choice modal) toggle independently on top of whichever is active.
        private void ShowOnly(GameObject target)
        {
            if (_soundSettings != null && _soundSettings.activeSelf && target != _mainMenuScreen)
                CloseSoundSettings();
            _nicknameScreen.SetActive(target == _nicknameScreen);
            _mainMenuScreen.SetActive(target == _mainMenuScreen);
            _profileScreen.Root.SetActive(target == _profileScreen.Root);
            _randomMatchScreen.SetActive(target == _randomMatchScreen);
            _friendMatchScreen.SetActive(target == _friendMatchScreen);
            _heroSelectScreen.SetActive(target == _heroSelectScreen);
            _setupScreen.SetActive(target == _setupScreen);
            _matchScreen.SetActive(target == _matchScreen);
            _timer.ShowBadge = target != _matchScreen; // the hourglass replaces the header badge in a match
            _surrenderButton.gameObject.SetActive(false); // re-shown once the match actually starts
            _headerSettingsButton.gameObject.SetActive(
                target == _heroSelectScreen || target == _setupScreen || target == _matchScreen);
        }

        private void OnSurrenderClicked()
        {
            _choiceOverlay.ShowMixed("本当に降伏しますか？\n降伏すると敗北として処理されます。",
                ("降伏", UITheme.Clay, Surrender),
                ("プレイを続ける", UITheme.Accent, () => { }));
        }

        private void Surrender()
        {
            const string message = ""; // my own surrender shows just the defeat title, no verdict line
            _stats.Surrendered = true;
            if (_isOnlineMatch)
            {
                _network.SubmitSurrender();
                EndOnlineGame(false, message);
            }
            else
            {
                EndGame(false, message);
            }
        }

        // A turn announcement that is still fading in/out would otherwise fire its
        // "start the turn" callback underneath the result popup.
        private void CancelTurnAnnouncement()
        {
            if (_turnAnnouncementRoutine != null)
            {
                StopCoroutine(_turnAnnouncementRoutine);
                _turnAnnouncementRoutine = null;
            }
            if (_turnAnnouncementRoot != null) _turnAnnouncementRoot.SetActive(false);
        }

        private void ShowNicknameScreen()
        {
            GameAudio.Music("BGM_Lobby_Noir");
            ShowOnly(_nicknameScreen);
            _headerText.text = "ライアーズ・バッティング";
            _tokenText.text = "";

            bool changing = !string.IsNullOrEmpty(_nickname);
            _nicknameTitle.text = changing ? "ニックネーム変更" : "ニックネーム設定";
            _nicknameSubtitle.text = changing
                ? $"現在のニックネーム: {_nickname}"
                : "一度設定すれば、次回からは直接メニューへ移動します。";
            _nicknameCancelButton.gameObject.SetActive(changing);
            _nicknameConfirmButton.interactable = true;
            _nicknameStatusText.text = "";
            _nicknameInput.text = "";
            _nicknameInput.ActivateInputField();
        }

        private void ShowMainMenu()
        {
            GameAudio.Music("BGM_Lobby_Noir");
            _timer.Stop();
            ShowOnly(_mainMenuScreen);
            _menuGreetingText.text = $"{_nickname}さん、ようこそ";
            _headerText.text = "ライアーズ・バッティング";
            _tokenText.text = "";
            StartGlobalInviteWatch();
            RefreshMenuProfileIcon();
        }

        // ---------- profile ----------

        private void BuildProfileScreen(Transform root)
        {
            _profileScreen = new ProfileScreen(root)
            {
                OnBack = ShowMainMenu,
                OnIconPicked = OnProfileIconPicked,
                OnRenameRequested = OnProfileRenameRequested,
                OnEquipRequested = OnCosmeticEquipRequested
            };
        }

        // Puts the equipped cosmetics on screen (placeholder looks: tints and an outline frame).
        private void ApplyCosmetics(PlayerProfile profile)
        {
            if (profile == null) return;
            var p = profile.Progress;

            // Background: the skin's art when it has one, else the default table tinted.
            var backgroundArt = CardArt.BackgroundSkin(p.Background);
            _rootBackground.sprite = backgroundArt != null ? backgroundArt : CardArt.Background();
            _rootBackground.color = backgroundArt != null
                ? Color.white
                : ProfileScreen.TintOf(RewardCatalog.Find(p.Background), Color.white);

            // Cards: the skin's card set when it has one, else the default cards tinted.
            CardArt.SetSkin(p.CardSkin == RewardCatalog.DefaultCards ? null : p.CardSkin,
                ProfileScreen.TintOf(RewardCatalog.Find(p.CardSkin), Color.white));
            _setupPicker.RefreshTint();
            _attackPicker.RefreshTint();

            // Portrait frame on my match portrait and my menu picture.
            _myHeroPortrait.SetFrame(p.Frame, p.Frame == RewardCatalog.NoFrame
                ? (Color?)null
                : ProfileScreen.TintOf(RewardCatalog.Find(p.Frame), Color.white));
            PortraitFrame.Apply(_menuProfileIcon, p.Frame);
        }

        private void OnCosmeticEquipRequested(RewardInfo reward)
        {
            _profileScreen.SetCosmeticsStatus("保存中...", false);
            _profiles.Equip(_nickname, reward, () =>
            {
                _profileScreen.ShowProfile(_profiles.Current);
                _profileScreen.SetCosmeticsStatus($"「{reward.Name}」を装備", false);
                ApplyCosmetics(_profiles.Current);
            }, error =>
            {
                _profileScreen.ShowProfile(_profiles.Current);
                _profileScreen.SetCosmeticsStatus($"保存に失敗しました: {error}", true);
            });
        }

        private void RefreshMenuProfileIcon()
        {
            string nickname = _nickname;
            _profiles.Load(nickname, profile =>
            {
                if (nickname != _nickname) return;   // renamed meanwhile
                ApplyCosmetics(profile);
                _menuProfileIcon.sprite = ProfileScreen.PortraitOf(profile.ProfileIcon);
            }, error => Debug.LogWarning($"プロフィールの読み込み失敗: {error}"));
        }

        private void ShowProfileScreen()
        {
            ShowOnly(_profileScreen.Root);
            _headerText.text = "プロフィール";
            _tokenText.text = "";
            _profileScreen.ShowLoading(_nickname);
            _profiles.Load(_nickname, profile =>
                {
                    _profileScreen.ShowProfile(profile);
                    ApplyCosmetics(profile);
                },
                error => _profileScreen.SetStatus($"プロフィールの読み込みに失敗しました: {error}", true));
        }

        private void OnProfileIconPicked(HeroId hero)
        {
            _profileScreen.SetInteractable(false);
            _profiles.SaveProfileIcon(_nickname, hero, () =>
            {
                _profileScreen.SetSelectedIcon(hero);
                _profileScreen.SetInteractable(true);
                _profileScreen.SetStatus("プロフィールアイコンを変更しました。", false);
                _menuProfileIcon.sprite = ProfileScreen.PortraitOf(hero);
            }, error =>
            {
                _profileScreen.SetInteractable(true);
                _profileScreen.SetStatus($"保存に失敗しました: {error}", true);
            });
        }

        private void OnProfileRenameRequested(string newName)
        {
            if (newName == _nickname) { _profileScreen.SetStatus("現在使用中のニックネームです。", true); return; }

            _profileScreen.SetInteractable(false);
            _profileScreen.SetStatus("変更中...", false);
            _profiles.Rename(_nickname, newName, () =>
            {
                _nickname = newName;
                PlayerPrefs.SetString(EffectiveNicknameKey(), _nickname);
                PlayerPrefs.Save();
                StartGlobalInviteWatch();   // invites now arrive under the new name
                _profileScreen.ClearRenameInput();
                if (_profiles.Current != null) _profileScreen.ShowProfile(_profiles.Current);
                _profileScreen.SetInteractable(true);
                _profileScreen.SetStatus("ニックネームを変更しました。", false);
            }, error =>
            {
                _profileScreen.SetInteractable(true);
                _profileScreen.SetStatus(error, true);
            });
        }

        // AI-mode entry point (the "AIマッチング" button). Picks the AI's hero and
        // (if it's DemonHunter) restricts its own guessing model right away,
        // then hands off to the shared hero-select screen.
        private void StartAiMatch()
        {
            GameAudio.Play("SFX_Match_Found");
            _isOnlineMatch = false;
            _matchOver = false;
            _inviteWatcher?.StopPolling();

            _state = new GameState();
            _stats = new MatchStats();
            _ai = new AiOpponent();
            _state.AiHero = RandomHeroId();
            _state.AiAbilityCharges = Mathf.Max(0, HeroCatalog.Get(_state.AiHero).Charges);
            if (_state.AiHero == HeroId.DemonHunter) _ai.RestrictGuessingPoolExclude9();

            ShowHeroSelectScreen();
        }

        private void OnSecretChosen(int[] playerSecret)
        {
            _timer.Stop();
            _setupPicker.ResetAll(); // no-op if Submit() already cleared it; needed when a timeout picked the secret instead

            if (_isOnlineMatch) { OnOnlineSecretChosen(playerSecret); return; }

            _state.PlayerSecret = playerSecret;

            GameAudio.Music("BGM_Match_Tension");
            ShowOnly(_matchScreen);
            StartMatchClock();
            _surrenderButton.gameObject.SetActive(true);
            _myHeroPortrait.SetHero(_state.PlayerHero);
            _opponentHeroPortrait.SetHero(_state.AiHero);
            _opponentHeroPortrait.SetSubtitle("AI");
            _myHeroPortrait.SetLook(_state.PlayerHero, _profiles.Current?.Progress);
            _myHeroPortrait.SetSubtitle(RewardCatalog.TitleText(_profiles.Current?.Progress.Title));
            _opponentHeroPortrait.SetLook(_state.AiHero, null);
            _myTurnActive = false;
            RefreshMyAbilityUI();
            _statusPanel.ShowMySecret(playerSecret);
            _statusPanel.OpponentAttackHistory.Clear();
            _statusPanel.MyAttackHistory.Clear();
            RefreshRevealedRows();

            bool aiFirst = UnityEngine.Random.value > 0.5f;
            ShowFirstTurnBanner(!aiFirst, () =>
            {
                if (aiFirst) BeginAiAttackTurn();
                else BeginPlayerAttackTurn();
            });
        }

        private void OnOnlineSecretChosen(int[] playerSecret)
        {
            _state.PlayerSecret = playerSecret;
            _state.AiSecret = new int[4]; // opponent's secret is never known locally; filled in one digit at a time by reveals

            GameAudio.Music("BGM_Match_Tension");
            ShowOnly(_matchScreen);
            StartMatchClock();
            _myHeroPortrait.SetHero(_state.PlayerHero);
            _opponentHeroPortrait.SetHero(_state.AiHero);
            _opponentHeroPortrait.SetSubtitle(_opponentTitle);
            _myHeroPortrait.SetLook(_state.PlayerHero, _profiles.Current?.Progress);
            _myHeroPortrait.SetSubtitle(RewardCatalog.TitleText(_profiles.Current?.Progress.Title));
            _opponentHeroPortrait.SetLook(_state.AiHero, _opponentProgress);
            _myTurnActive = false;
            RefreshMyAbilityUI();
            _statusPanel.ShowMySecret(playerSecret);
            _statusPanel.OpponentAttackHistory.Clear();
            _statusPanel.MyAttackHistory.Clear();
            RefreshRevealedRows();

            _attackPicker.SetInteractable(false);
            _headerText.text = $"{_network.OpponentNickname}さんが暗証番号を設定中...";
            _tokenText.text = "";

            _network.MarkMySecretReady();
        }

        private void RefreshTokenHeader()
        {
            // The opponent's remaining LIE TOKEN count is exactly the kind of
            // information the game is designed to hide -- only my own is shown.
            _tokenText.text = "";
            _statusPanel.LieTokens.Set(_state.PlayerLieTokens);
            // Paladin's heal button depends on the token count (disabled while full).
            RefreshMyAbilityUI();
        }

        // Bumped whenever a match ends. A delayed step scheduled before that (the AI "thinking",
        // a turn handover) is dropped instead of firing underneath the result popup -- which
        // matters now that a match can end at any moment by surrendering.
        private int _matchSession;

        private void RunAfter(float seconds, Action action)
        {
            int session = _matchSession;
            StartCoroutine(RunAfterRoutine(seconds, () =>
            {
                if (session == _matchSession) action?.Invoke();
            }));
        }

        private const float TurnAnnouncementHoldSeconds = 0.55f;
        private const float TimeoutNoticeHoldSeconds = 1.1f;
        private const float FirstTurnBannerHoldSeconds = 1.6f;
        private const float NoticeHoldSeconds = 1.5f;          // "the opponent trusted you" and the like
        private const float RevealNoticeHoldSeconds = 2.4f;    // a digit was opened: long enough to read it and see the card

        private void ShowTurnAnnouncement(string message, Action onComplete)
        {
            GameAudio.Play(message == "自分のターン" ? "SFX_Turn_Start_My" : "SFX_Turn_Start_Opponent");
            ShowBanner(message, TurnAnnouncementHoldSeconds, onComplete);
        }

        // Who moves first, big and in the middle of the screen, right as the match starts.
        private void ShowFirstTurnBanner(bool iAmFirst, Action onComplete)
        {
            string headline = iAmFirst ? "先攻" : "後攻";
            string detail = iAmFirst ? "自分が先に相手の暗証番号を推測します" : "相手が先に自分の暗証番号を推測します";
            ShowBanner($"<size=110>{headline}</size>\n<size=34>{detail}</size>", FirstTurnBannerHoldSeconds, onComplete);
        }

        // Text for "a digit of the opponent's secret was just opened" -- the card itself is drawn
        // under it by the banner (see ShowBanner's cardDigit).
        private static string RevealNoticeText(string headline, string detail, int index) =>
            $"<size=64>{headline}</size>\n<size=30>{detail}</size>\n<size=26>相手の暗証番号の {index + 1} 桁目が公開されました</size>";

        // Online, the notice has to wait for the turn change that arrives in the same poll
        // (OnOpponentTrusted / OnOpponentRevealed fire right before OnTurnChanged): it's shown by
        // UpdateOnlineTurnUI, ahead of the turn banner, instead of being cut off by it.
        private void QueueNotice(string text, float holdSeconds, int cardDigit = -1)
        {
            _pendingNotice = text;
            _pendingNoticeHold = holdSeconds;
            _pendingNoticeDigit = cardDigit;
        }

        private static bool AllRevealed(bool[] revealed)
        {
            foreach (bool r in revealed) if (!r) return false;
            return true;
        }

        private static int HiddenCount(bool[] revealed)
        {
            int n = 0;
            foreach (bool r in revealed) if (!r) n++;
            return n;
        }

        // Shown when a timed decision runs out, so the player sees what was picked
        // for them before the game moves on (and before the turn-change banner).
        // Shares the turn banner's full-screen backdrop, which also blocks clicks
        // meanwhile, and its cancellation (CancelTurnAnnouncement on game end).
        private void ShowTimeoutNotice(string whatHappened, Action onComplete)
        {
            ShowBanner($"時間切れ！\n<size=24>{whatHappened}</size>", TimeoutNoticeHoldSeconds, onComplete);
        }

        // cardDigit >= 0 draws that card under the text (a digit that was just opened).
        private void ShowBanner(string message, float holdSeconds, Action onComplete, int cardDigit = -1)
        {
            if (_turnAnnouncementRoot == null)
            {
                _turnAnnouncementRoot = new GameObject("TurnAnnouncement", typeof(RectTransform), typeof(Image));
                var root = (RectTransform)_turnAnnouncementRoot.transform;
                root.SetParent(_matchScreen.transform.parent, false);
                UiFactory.StretchToFillParent(root);
                _turnAnnouncementBackdrop = _turnAnnouncementRoot.GetComponent<Image>();
                _turnAnnouncementBackdrop.color = new Color(0f, 0f, 0f, 0f);
                _turnAnnouncementText = UiFactory.Text(root, "", 36, Color.white, TextAnchor.MiddleCenter, FontStyle.Bold);
                _turnAnnouncementText.raycastTarget = false;
                UiFactory.StretchToFillParent((RectTransform)_turnAnnouncementText.transform);

                var cardGo = new GameObject("RevealCard", typeof(RectTransform), typeof(Image));
                var cardRt = (RectTransform)cardGo.transform;
                cardRt.SetParent(root, false);
                cardRt.anchorMin = new Vector2(0.5f, 0.3f);
                cardRt.anchorMax = new Vector2(0.5f, 0.3f);
                cardRt.pivot = new Vector2(0.5f, 0.5f);
                cardRt.sizeDelta = new Vector2(120f, 168f);   // card art is 5:7
                _turnAnnouncementCard = cardGo.GetComponent<Image>();
                _turnAnnouncementCard.preserveAspect = true;
                _turnAnnouncementCard.raycastTarget = false;
                cardGo.SetActive(false);

                _turnAnnouncementRoot.SetActive(false);
            }

            if (_turnAnnouncementRoutine != null) StopCoroutine(_turnAnnouncementRoutine);

            // With a card the text moves up to make room for it; without one it fills the screen.
            var textRt = (RectTransform)_turnAnnouncementText.transform;
            textRt.anchorMin = cardDigit >= 0 ? new Vector2(0f, 0.47f) : Vector2.zero;
            textRt.anchorMax = cardDigit >= 0 ? new Vector2(1f, 0.92f) : Vector2.one;
            textRt.offsetMin = Vector2.zero;
            textRt.offsetMax = Vector2.zero;
            if (cardDigit >= 0)
            {
                _turnAnnouncementCard.sprite = CardArt.Digit(cardDigit);
                _turnAnnouncementCard.color = CardArt.Tint;
            }
            _turnAnnouncementCard.gameObject.SetActive(cardDigit >= 0);

            _turnAnnouncementText.text = message;
            _turnAnnouncementRoot.SetActive(true);
            _turnAnnouncementRoot.transform.SetAsLastSibling();
            SetTurnAnnouncementAlpha(0f);
            _turnAnnouncementRoutine = StartCoroutine(TurnAnnouncementRoutine(holdSeconds, onComplete));
        }

        private System.Collections.IEnumerator TurnAnnouncementRoutine(float holdDuration, Action onComplete)
        {
            const float fadeDuration = 0.3f;
            float elapsed = 0f;
            while (elapsed < fadeDuration)
            {
                elapsed += Time.unscaledDeltaTime;
                SetTurnAnnouncementAlpha(Mathf.Clamp01(elapsed / fadeDuration));
                yield return null;
            }
            SetTurnAnnouncementAlpha(1f);

            yield return new WaitForSecondsRealtime(holdDuration);

            elapsed = 0f;
            while (elapsed < fadeDuration)
            {
                elapsed += Time.unscaledDeltaTime;
                SetTurnAnnouncementAlpha(1f - Mathf.Clamp01(elapsed / fadeDuration));
                yield return null;
            }
            _turnAnnouncementRoot.SetActive(false);
            _turnAnnouncementRoutine = null;
            onComplete?.Invoke();
        }

        private void SetTurnAnnouncementAlpha(float alpha)
        {
            var backdropColor = _turnAnnouncementBackdrop.color;
            backdropColor.a = 0.72f * alpha;
            _turnAnnouncementBackdrop.color = backdropColor;

            var textColor = _turnAnnouncementText.color;
            textColor.a = alpha;
            _turnAnnouncementText.color = textColor;

            var cardColor = _turnAnnouncementCard.color;
            cardColor.a = alpha;
            _turnAnnouncementCard.color = cardColor;
        }

        private System.Collections.IEnumerator RunAfterRoutine(float seconds, Action action)
        {
            yield return new WaitForSeconds(seconds);
            action?.Invoke();
        }

        private void BeginPlayerAttackTurn(bool announce = true)
        {
            GameAudio.Music("BGM_Match_Tension");
            if (announce)
            {
                _stats.OnExtraTurn = false;   // an announced turn is a normal one
                _timer.Stop();
                _attackPicker.SetInteractable(false);
                ShowTurnAnnouncement("自分のターン", () => BeginPlayerAttackTurn(false));
                return;
            }
            RefreshTokenHeader();
            _headerText.text = "自分のターン — 相手の暗証番号を推測してください";
            _attackPicker.SetInteractable(true);
            SetMyTurnActive(true);
            _timer.Start(GuessSeconds, () =>
            {
                // e.g. the Priest position/digit picker may still be open.
                _choiceOverlay.Hide();
                _attackPicker.SetInteractable(false);
                var randomGuess = RandomDigits(false);
                ShowTimeoutNotice($"ランダムな数字 {string.Join(" ", randomGuess)} で推測します",
                    () => OnPlayerGuessSubmitted(randomGuess));
            });
        }

        // Warrior's keep-turn flag (armed via the ability icon) is consumed
        // here, at the moment my attack turn actually ends -- whichever of the
        // several call sites that is (trust, catch-a-lie reveal, wrong-
        // accusation reveal, or a Priest query answer).
        private void EndMyAttackTurnAndPassToOpponent()
        {
            if (_state.PlayerExtraTurnPending)
            {
                _state.PlayerExtraTurnPending = false;
                _stats.OnExtraTurn = true;   // this next attack is the Warrior's kept turn
                BeginPlayerAttackTurn(false);
            }
            else
            {
                BeginAiAttackTurn();
            }
        }

        private void EndAiAttackTurnAndPassToPlayer()
        {
            if (_state.AiHero == HeroId.Warrior && _ai.ShouldUseWarriorExtraTurn(_state.AiAbilityCharges))
            {
                _state.AiAbilityCharges--;
                BeginAiAttackTurn(false);
            }
            else
            {
                _headerText.text = "相手のターンが終了しました...";
                RunAfter(TurnHandoverDelaySeconds, () => BeginPlayerAttackTurn());
            }
        }

        // AI-mode-only: the AI has full access to both secrets in this same
        // process, so Wizard's mutual reveal needs no network round-trip here
        // (unlike UseWizardAbility's online branch).
        private void AiUseWizard()
        {
            _state.AiAbilityCharges--;
            int aiIdx = _ai.PickRevealIndex(_state.AiRevealed);
            if (aiIdx >= 0) _state.AiRevealed[aiIdx] = true;
            int playerIdx = _ai.PickRevealIndex(_state.PlayerRevealed);
            if (playerIdx >= 0) _state.PlayerRevealed[playerIdx] = true;
            RefreshRevealedRows();
        }

        // Every digit of a secret opened means the other side knows it completely: that side loses.
        // Both return true when they ended the match, so the caller must stop its own flow.
        private bool EndAiMatchIfPlayerFullyRevealed()
        {
            if (_matchOver || !AllRevealed(_state.PlayerRevealed)) return false;
            EndGame(false, "自分の暗証番号がすべて公開された");
            return true;
        }

        private bool EndAiMatchIfAiFullyRevealed()
        {
            if (_matchOver || !AllRevealed(_state.AiRevealed)) return false;
            EndGame(true, "相手の暗証番号がすべて公開された");
            return true;
        }

        private void OnPlayerGuessSubmitted(int[] guess)
        {
            _stats.Guesses++;
            _timer.Stop();
            _attackPicker.ResetAll();

            if (_isOnlineMatch) { OnOnlineGuessSubmitted(guess); return; }

            var trueResult = Judge.Evaluate(_state.AiSecret, guess);
            bool canLieAboutWin = _state.AiHero == HeroId.Hunter;

            if (_ai.ShouldUsePaladinHeal(_state.AiLieTokens, _state.AiAbilityCharges) && _state.AiHero == HeroId.Paladin)
            {
                _state.AiAbilityCharges--;
                _state.AiLieTokens = Mathf.Min(_state.AiLieTokens + 1, GameState.MaxLieTokens);
            }

            bool aiLied = _ai.ShouldLieAsDefender(trueResult, _state.AiLieTokens, canLieAboutWin);
            JudgeResult reported;
            if (aiLied)
            {
                reported = _ai.FabricateResult(trueResult);
                _state.AiLieTokens--;
            }
            else
            {
                reported = trueResult;
            }
            if (trueResult.IsWin && !aiLied) _state.Result = GameResult.PlayerWin;

            var playerRecord = new TurnRecord(guess, trueResult, reported);
            _state.PlayerAttackHistory.Add(playerRecord);
            _attackPicker.SetInteractable(false);
            // revealLie stays false here: this is what the AI told the player about
            // the player's OWN guess, and whether that was a lie must stay hidden.
            _statusPanel.MyAttackHistory.AddRow(guess, reported, playerRecord.WasLie, revealLie: false);
            RefreshTokenHeader();

            if (_state.Result == GameResult.PlayerWin)
            {
                _stats.WonByGuess = true;
                _stats.WonOnExtraTurn = _stats.OnExtraTurn;
                EndGame(true);
                return;
            }

            _headerText.text = "相手が判定中...";
            RunAfter(AiJudgeDelaySeconds, () => PromptTrustOrChallenge(reported, aiLied,
                onTrust: EndMyAttackTurnAndPassToOpponent,
                onChallenge: () =>
                {
                    _stats.Challenges++;
                    if (aiLied) _stats.ChallengesCorrect++; else _stats.ChallengesWrong++;
                    if (aiLied)
                    {
                        GameAudio.Play("SFX_Reveal_Busted");
                        // Caught it: the AI has to open one of its own digits.
                        _statusPanel.MyAttackHistory.MarkLastRowAsLie();
                        RevealAiDigit("的中！", "相手が嘘をついていました", EndMyAttackTurnAndPassToOpponent);
                    }
                    else
                    {
                        GameAudio.Play("SFX_Reveal_Punished");
                        // Wrong accusation: the player opens one of their own digits.
                        // The opponent's answer is now proven true -- keep that in the log.
                        _statusPanel.MyAttackHistory.MarkLastRowAsTruth();
                        PromptPlayerRevealChoice("見当違い！相手は真実を述べていました。\nペナルティとして自分の暗証番号の1桁を公開してください。",
                            _ =>
                            {
                                if (EndAiMatchIfPlayerFullyRevealed()) return;   // that was my last hidden digit
                                EndMyAttackTurnAndPassToOpponent();
                            });
                    }
                }));
        }

        // The opponent "thinks" for a moment before its guess shows up.
        private void BeginAiAttackTurn(bool announce = true)
        {
            GameAudio.Music("BGM_Match_Tension");
            if (announce)
            {
                _timer.Stop();
                _attackPicker.SetInteractable(false);
                ShowTurnAnnouncement("相手のターン", () => BeginAiAttackTurn(false));
                return;
            }
            RefreshTokenHeader();
            _headerText.text = "相手のターン — 相手が推測中...";
            _attackPicker.SetInteractable(false);
            SetMyTurnActive(false);
            RunAfter(AiGuessDelaySeconds, BeginAiAttackTurnNow);
        }

        private void BeginAiAttackTurnNow()
        {
            RefreshTokenHeader();
            _headerText.text = "相手のターン — 防御結果を確認してください";
            _attackPicker.SetInteractable(false);
            SetMyTurnActive(false);

            // The AI only opens its own digit when that won't be its last hidden one (it would lose).
            if (_state.AiHero == HeroId.Wizard && _ai.ShouldUseWizardReveal(_state.AiAbilityCharges)
                && HiddenCount(_state.AiRevealed) >= 2)
            {
                AiUseWizard();
                if (EndAiMatchIfPlayerFullyRevealed()) return;   // its ability opened my last hidden digit
            }

            if (_state.AiHero == HeroId.Priest && _ai.ShouldUsePriestQuery(_state.AiAbilityCharges))
            {
                AiUsePriestQuery();
                return;
            }

            var guess = _ai.NextGuess();
            var trueResult = Judge.Evaluate(_state.PlayerSecret, guess);
            bool canLieAboutWin = _state.PlayerHero == HeroId.Hunter;

            PromptDefenseChoice(guess, trueResult, _state.PlayerLieTokens, canLieAboutWin, (reported, usedToken) =>
            {
                if (usedToken) _state.PlayerLieTokens--;
                bool playerLied = !reported.Equals(trueResult);
                var record = new TurnRecord(guess, trueResult, reported);
                _state.AiAttackHistory.Add(record);
                _statusPanel.OpponentAttackHistory.AddRow(guess, reported, record.WasLie);

                RefreshTokenHeader();

                if (reported.IsWin)
                {
                    _state.Result = GameResult.AiWin;
                    _ai.NarrowByOwnGuess(guess, reported);
                    EndGame(false);
                    return;
                }

                // The AI decides for itself whether to trust or challenge. Rogue
                // lets it just directly know the truth instead of guessing at it.
                bool challenge;
                if (_state.AiHero == HeroId.Rogue && _ai.ShouldUseRogueDetect(_state.AiAbilityCharges))
                {
                    _state.AiAbilityCharges--;
                    challenge = playerLied;
                }
                else
                {
                    challenge = _ai.ShouldChallengeAsAttacker(guess, reported);
                }

                if (!challenge)
                {
                    if (playerLied) _stats.LieBelieved();
                    _ai.NarrowByOwnGuess(guess, reported);
                    // Say what the opponent decided -- the turn used to just move on without a word.
                    GameAudio.Play("SFX_Trust_Accept");
                    ShowBanner("<size=48>相手があなたの回答を信じました</size>", NoticeHoldSeconds,
                        EndAiAttackTurnAndPassToPlayer);
                    return;
                }

                if (playerLied)
                {
                    _stats.LiesCaught++;
                    GameAudio.Play("SFX_Reveal_Punished");
                    // Caught: the reported value is known-false, so there's nothing
                    // honest to narrow the AI's model by this turn -- it only gains
                    // the digit reveal, not extra information.
                    PromptPlayerRevealChoice("相手があなたの嘘を見破りました！\nペナルティとして自分の暗証番号の1桁を公開してください。",
                        _ =>
                        {
                            if (EndAiMatchIfPlayerFullyRevealed()) return;   // that was my last hidden digit
                            EndAiAttackTurnAndPassToPlayer();
                        });
                }
                else
                {
                    GameAudio.Play("SFX_Reveal_Busted");
                    // Wrong accusation: the AI opens one of its own digits, but the
                    // (confirmed-true) result is still good information to narrow by.
                    _ai.NarrowByOwnGuess(guess, reported);
                    RevealAiDigit("相手の見当違い！", "相手があなたを疑いましたが外れでした", EndAiAttackTurnAndPassToPlayer);
                }
            });
        }

        // AI-mode Priest query: the AI asks "is position X digit Y?" instead of
        // making a normal guess this turn, picking the digit most consistent
        // with its remaining candidates rather than a random shot. The player
        // (as defender) answers via the same PromptPriestDefenseChoice used for
        // the online opponent-query path.
        private void AiUsePriestQuery()
        {
            _state.AiAbilityCharges--;
            var (position, digit) = _ai.PickPriestQuery(_state.PlayerRevealed);
            if (position < 0) { EndAiAttackTurnAndPassToPlayer(); return; }
            PromptPriestDefenseChoice(position, digit, answer => EndAiAttackTurnAndPassToPlayer());
        }

        // Shared by both directions: forces open one of the AI's own digits and announces it with a big
        // banner (what happened, which position, and the card itself) before continuing. If that was the
        // AI's last hidden digit the player has won, and the banner leads into the result instead.
        private void RevealAiDigit(string headline, string detail, Action onDone)
        {
            int idx = _ai.PickRevealIndex(_state.AiRevealed);
            if (idx < 0) { onDone(); return; }

            _state.AiRevealed[idx] = true;
            RefreshRevealedRows();
            string text = RevealNoticeText(headline, detail, idx);
            ShowBanner(text, RevealNoticeHoldSeconds, () =>
            {
                if (EndAiMatchIfAiFullyRevealed()) return;
                onDone();
            }, _state.AiSecret[idx]);
        }

        // Lets the player pick which of their own still-hidden digits to open.
        // onRevealed gets the chosen index (or -1 if every digit was already
        // open, in which case there's nothing to actually reveal).
        private void PromptPlayerRevealChoice(string message, Action<int> onRevealed)
        {
            var open = new List<int>();
            for (int i = 0; i < 4; i++)
                if (!_state.PlayerRevealed[i]) open.Add(i);

            if (open.Count == 0)
            {
                _choiceOverlay.Show($"{message} (既にすべての桁が公開済み)", ("確認", () => onRevealed(-1)));
                return;
            }

            var options = new (string, Action)[open.Count];
            for (int n = 0; n < open.Count; n++)
            {
                int i = open[n];
                options[n] = ($"{i + 1}桁目: {_state.PlayerSecret[i]}", () =>
                {
                    _state.PlayerRevealed[i] = true;
                    // Only set in AI mode -- in an online match there's no local
                    // AiOpponent to keep in sync, the opponent is a real client.
                    _ai?.NarrowByRevealedDigit(i, _state.PlayerSecret[i]);
                    RefreshRevealedRows();
                    onRevealed(i);
                });
            }
            _choiceOverlay.Show(message, options);
        }

        private void RefreshRevealedRows()
        {
            _statusPanel.OpponentRevealed.Refresh(_state.AiSecret, _state.AiRevealed);
            _statusPanel.MyRevealed.Refresh(_state.PlayerSecret, _state.PlayerRevealed);
            RefreshMyAbilityUI();   // the Wizard button depends on how many of my digits are still hidden
        }

        // Shows the true result of the opponent's guess (computed against my real
        // secret) and lets me choose to report it truthfully or spend a LIE TOKEN.
        // A true 4-strike normally can't be lied about -- Hunter is the exception.
        // Owns its own 20s timer: defaults to reporting the truth on timeout.
        private void PromptDefenseChoice(int[] opponentGuess, JudgeResult trueResult, int lieTokensLeft,
            bool canLieAboutWin, Action<JudgeResult, bool> onChoice)
        {
            // The opponent guessed my secret exactly (4S 0B). Only Hunter, with a LIE TOKEN to spend, may
            // deny it; for everyone else there is no decision to make, so it's an immediate defeat rather
            // than a popup that just asks to click "send the truth".
            if (trueResult.IsWin && !(canLieAboutWin && lieTokensLeft > 0))
            {
                onChoice(trueResult, false);
                return;
            }

            void Resolve(JudgeResult result, bool usedToken)
            {
                _lastDefenseWasLie = usedToken;
                if (usedToken) _stats.LiesTold++;
                if (usedToken && trueResult.IsWin) _stats.HidTrueWin = true;   // Hunter: hid a real 4-strike
                _timer.Stop();
                GameAudio.Play(usedToken ? "SFX_LieToken_Spend" : "SFX_Truth_Declare");
                onChoice(result, usedToken);
            }

            string resultText = trueResult.IsOut ? "OUT" : $"{trueResult.Strike}S {trueResult.Ball}B";
            string message = $"相手の推測: {string.Join(" ", opponentGuess)}\n実際の結果: {resultText}";

            bool canLie = lieTokensLeft > 0 && (!trueResult.IsWin || canLieAboutWin);
            if (!canLie)
            {
                _choiceOverlay.Show(message, ("真実を送信", () => Resolve(trueResult, false)));
            }
            else
            {
                _choiceOverlay.ShowMixed(message,
                    ("真実を送信", UITheme.Accent, () => Resolve(trueResult, false)),
                    ($"LIE TOKEN使用 ({lieTokensLeft}個)", UITheme.Clay,
                        () => PromptFakeStrikeChoice(trueResult, (r, u) => Resolve(r, u))));
            }

            // The overlay may be on any of the three steps (truth/lie, strike, ball) here.
            _timer.Start(DefendChoiceSeconds, () =>
            {
                _choiceOverlay.Hide();
                ShowTimeoutNotice("真実を送信します", () => Resolve(trueResult, false));
            });
        }

        // Choosing a fake result is two short steps (strike, then ball) instead of
        // one wall of up to a dozen "2S1B"-style buttons -- easier to scan, and it
        // never needs more than 4-5 buttons on screen at once. The defend-choice
        // timer (started by the caller, PromptDefenseChoice) keeps running through
        // both of these steps and still falls back to the truth if it expires here.
        private void PromptFakeStrikeChoice(JudgeResult trueResult, Action<JudgeResult, bool> onChoice)
        {
            var allFakes = Judge.PlausibleFakeResults(trueResult);
            var strikeOptions = new List<int>();
            foreach (var f in allFakes)
                if (!strikeOptions.Contains(f.Strike)) strikeOptions.Add(f.Strike);
            strikeOptions.Sort();

            var buttons = new (string, Color, Action)[strikeOptions.Count];
            for (int i = 0; i < strikeOptions.Count; i++)
            {
                int s = strikeOptions[i];
                buttons[i] = ($"{s} ストライク", UITheme.StrikeFg, () => PromptFakeBallChoice(s, allFakes, onChoice));
            }
            _choiceOverlay.ShowMixed("嘘: ストライクをいくつと伝えますか？", buttons);
        }

        private void PromptFakeBallChoice(int strike, List<JudgeResult> allFakes, Action<JudgeResult, bool> onChoice)
        {
            var matching = allFakes.FindAll(f => f.Strike == strike);
            if (matching.Count == 1)
            {
                onChoice(matching[0], true);
                return;
            }

            var buttons = new (string, Color, Action)[matching.Count];
            for (int i = 0; i < matching.Count; i++)
            {
                var fake = matching[i];
                string label = fake.IsOut ? "OUT" : $"{fake.Ball} ボール";
                buttons[i] = (label, UITheme.BallFg, () => onChoice(fake, true));
            }
            _choiceOverlay.ShowMixed($"嘘: {strike} ストライクに決めました。ボールはいくつと伝えますか？", buttons);
        }

        // Shared trust/challenge decision (point 4). Owns its own 20s timer:
        // defaults to trusting the report on timeout.
        // knownLieStatusForRogue: AI mode already knows the answer locally (pass
        // it directly); online mode passes null and the Rogue button triggers a
        // _network.SubmitRogueQuery() round-trip instead.
        private void PromptTrustOrChallenge(JudgeResult reported, bool? knownLieStatusForRogue,
            Action onTrust, Action onChallenge)
        {
            GameAudio.Play("SFX_Response_Received");
            RunAfter(0.2f, () =>
            {
                if (reported.IsOut) GameAudio.Play("SFX_Judge_Out");
                else if (reported.Strike > 0) GameAudio.Play("SFX_Judge_Strike");
                else if (reported.Ball > 0) GameAudio.Play("SFX_Judge_Ball");
            });
            if (reported.Strike > 0 && reported.Ball > 0)
                RunAfter(0.45f, () => GameAudio.Play("SFX_Judge_Ball"));
            GameAudio.Music(reported.Strike == 3 ? "BGM_Match_Climax" : "BGM_Match_Tension");

            void ResolveTrust() { _timer.Stop(); GameAudio.Play("SFX_Trust_Accept"); onTrust(); }
            void ResolveChallenge() { _timer.Stop(); GameAudio.Play("SFX_Challenge_Doubt"); onChallenge(); }

            string resultText = reported.IsOut ? "OUT" : $"{reported.Strike}S {reported.Ball}B";
            string message = $"相手から伝えられた結果: {resultText}\n信じますか？嘘だと思いますか？";
            bool rogueAvailable = _state.PlayerHero == HeroId.Rogue && _state.PlayerAbilityCharges > 0;

            var options = new List<(string, Color, Action)>
            {
                ("信じる", UITheme.Accent, (Action)ResolveTrust),
                ("嘘だと思う（ダウト）", UITheme.Clay, (Action)ResolveChallenge)
            };

            if (rogueAvailable)
            {
                // Rogue already knows the answer for certain, so there's nothing
                // left to decide -- using it just tells you the truth and acts on
                // it immediately (challenge if it was a lie, trust if it wasn't),
                // instead of asking you to pick again after being told the answer.
                options.Add(($"真実看破 ({_state.PlayerAbilityCharges}回)", UITheme.StrikeFg, (Action)(() =>
                {
                    _timer.Stop();
                    GameAudio.Play("SFX_Hero_Rogue_Insight");
                    _stats.AbilityUses++;
                    _state.PlayerAbilityCharges--;
                    RefreshMyAbilityUI();

                    if (knownLieStatusForRogue.HasValue)
                    {
                        // AI mode knows the answer immediately -- but the overlay's
                        // own button click handler is still on the call stack right
                        // here (ChoiceOverlayView.ShowMixed closes it before invoking
                        // the click), so rebuilding it again in the same frame is
                        // what threw Unity's InvalidOperationException. One frame
                        // of delay lets that click finish first.
                        StartCoroutine(ShowRogueResultNextFrame(knownLieStatusForRogue.Value));
                    }
                    else
                    {
                        // The network answer always arrives on a later frame anyway
                        // (a poll callback), so no extra delay is needed here.
                        Action<bool> handler = null;
                        handler = wasLie =>
                        {
                            _network.OnRogueAnswer -= handler;
                            _timer.Stop();
                            ShowRogueResult(wasLie);
                        };
                        _network.OnRogueAnswer += handler;
                        _network.SubmitRogueQuery();
                        _headerText.text = "真実看破 — 相手の応答を確認中...";

                        // The opponent answers automatically, but only if its client is still
                        // mid-defense and the query reached it. Without an answer the wait used to
                        // hang forever: give the charge back and ask again instead.
                        _timer.Start(RogueAnswerTimeoutSeconds, () =>
                        {
                            _network.OnRogueAnswer -= handler;
                            if (_matchOver) return;
                            _state.PlayerAbilityCharges++;
                            _stats.AbilityUses--;
                            RefreshMyAbilityUI();
                            ShowTimeoutNotice("相手の応答がないため真実看破を取り消します",
                                () => PromptTrustOrChallenge(reported, null, onTrust, onChallenge));
                        });
                    }
                })));
            }

            void ShowRogueResult(bool wasLie)
            {
                if (wasLie) _stats.RogueCatches++;
                // The truth check is certain, so the log can record the verdict right away.
                if (wasLie) _statusPanel.MyAttackHistory.MarkLastRowAsLie();
                else _statusPanel.MyAttackHistory.MarkLastRowAsTruth();
                string resultMessage = wasLie ? "真実看破の結果: 嘘でした！" : "真実看破の結果: 真実でした！";
                _choiceOverlay.Show(resultMessage, ("確認", wasLie ? (Action)ResolveChallenge : ResolveTrust));
            }

            System.Collections.IEnumerator ShowRogueResultNextFrame(bool wasLie)
            {
                yield return null;
                ShowRogueResult(wasLie);
            }

            _choiceOverlay.ShowMixed(message, options.ToArray());
            _timer.Start(TrustChoiceSeconds, () =>
            {
                _choiceOverlay.Hide();
                ShowTimeoutNotice("「信じる」として処理します", ResolveTrust);
            });
        }

        private void EndGame(bool playerWon, string message = null)
        {
            if (_matchOver) return;
            _matchOver = true;
            _timer.Stop();
            _matchSession++;
            CancelTurnAnnouncement();
            _surrenderButton.gameObject.SetActive(false);
            GameAudio.Music(playerWon ? "BGM_Win_Stinger" : "BGM_Lose_Stinger", false);
            _resultPopup.Show(playerWon,
                message ?? (playerWon ? "相手の暗証番号を当てた" : "自分の暗証番号が見破られた"),
                playerWon ? _state.AiSecret : _state.PlayerSecret);
            SettleMatch(playerWon);
        }
    }
}
