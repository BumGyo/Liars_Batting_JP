using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using LiarsBatting.Core;
using LiarsBatting.AI;
using LiarsBatting.Network;
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
        private const float DefendChoiceSeconds = 10f;
        private const float TrustChoiceSeconds = 10f;

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
        private GameObject _mainMenuScreen;
        private GameObject _randomMatchScreen;
        private GameObject _friendMatchScreen;
        private GameObject _heroSelectScreen;
        private GameObject _setupScreen;
        private GameObject _matchScreen;
        private GameObject _gameOverScreen;

        private Text _randomMatchStatusText;
        private InputField _friendTargetInput;
        private Text _friendStatusText;

        private readonly List<Button> _heroSelectButtons = new List<Button>();

        private CardPickerView _setupPicker;
        private CardPickerView _attackPicker;
        private StatusPanelView _statusPanel;
        private ChoiceOverlayView _choiceOverlay;
        private CountdownTimerView _timer;
        private HeroPortraitView _myHeroPortrait;
        private HeroPortraitView _opponentHeroPortrait;
        private Button _priestButton;

        private Text _headerText;
        private Text _tokenText;
        private Text _gameOverText;
        private Text _menuGreetingText;

        private void Start()
        {
            UiFactory.EnsureEventSystem();
            var canvas = UiFactory.CreateCanvas();
            DontDestroyOnLoad(canvas.gameObject);

            var root = UiFactory.FullScreen(canvas.transform, "Root", UITheme.Bg);
            _firestore = new FirestoreClient(this);

            BuildHeader(root);
            BuildNicknameScreen(root);
            BuildMainMenuScreen(root);
            BuildRandomMatchScreen(root);
            BuildFriendMatchScreen(root);
            BuildHeroSelectScreen(root);
            BuildSetupScreen(root);
            BuildMatchScreen(root);
            BuildGameOverScreen(root);
            _choiceOverlay = new ChoiceOverlayView(root); // built last so it renders on top

            _nickname = PlayerPrefs.GetString(EffectiveNicknameKey(), "");
            if (string.IsNullOrEmpty(_nickname)) ShowNicknameScreen();
            else ShowMainMenu();
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

            _headerText = UiFactory.Text(row, "라이어스 배팅", 18, UITheme.Ink, TextAnchor.MiddleLeft, FontStyle.Bold);
            UiFactory.SetFlexible(_headerText, 1, 0);
            _timer = new CountdownTimerView(row, this);
            _tokenText = UiFactory.Text(row, "", 13, UITheme.Muted, TextAnchor.MiddleRight);
            UiFactory.SetSize(_tokenText, 220, 30);
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

        private void BuildNicknameScreen(Transform root)
        {
            _nicknameScreen = BuildFullScreenContainer(root, "NicknameScreen");

            var centered = UiFactory.VerticalGroup(_nicknameScreen.transform, "Centered", spacing: 16,
                padding: new RectOffset(40, 40, 40, 40));
            centered.anchorMin = new Vector2(0.28f, 0.32f);
            centered.anchorMax = new Vector2(0.72f, 0.68f);
            centered.offsetMin = Vector2.zero;
            centered.offsetMax = Vector2.zero;

            UiFactory.Text(centered, "닉네임을 정해주세요", 20, UITheme.Ink, TextAnchor.MiddleCenter, FontStyle.Bold);
            UiFactory.Text(centered, "한 번만 설정하면 다음부터는 바로 메뉴로 갑니다.", 13, UITheme.Muted, TextAnchor.MiddleCenter);

            var input = UiFactory.InputField(centered, "예: 라이어짱");
            var statusText = UiFactory.Text(centered, "", 12, UITheme.Clay, TextAnchor.MiddleCenter);
            statusText.gameObject.SetActive(false);

            Button confirmBtn = null;
            confirmBtn = UiFactory.Button(centered, "확인", UITheme.Accent, Color.white, () =>
            {
                string name = input.text.Trim();
                if (name.Length == 0)
                {
                    ShowNicknameStatus(statusText, "닉네임을 입력해주세요.", isError: true);
                    return;
                }

                confirmBtn.interactable = false;
                ShowNicknameStatus(statusText, "확인하는 중...", isError: false);

                _firestore.GetDocument($"players/{name}",
                    existing =>
                    {
                        if (existing != null)
                        {
                            confirmBtn.interactable = true;
                            ShowNicknameStatus(statusText, "이미 사용 중인 닉네임입니다.", isError: true);
                            return;
                        }
                        RegisterNickname(name, confirmBtn, statusText);
                    },
                    error =>
                    {
                        confirmBtn.interactable = true;
                        ShowNicknameStatus(statusText, $"서버 연결에 실패했습니다: {error}", isError: true);
                    });
            }, 15);
            UiFactory.SetHeight(confirmBtn, 44);
        }

        private void RegisterNickname(string name, Button confirmBtn, Text statusText)
        {
            var fields = new JObject
            {
                ["nickname"] = FirestoreClient.StringField(name),
                ["lastSeen"] = FirestoreClient.IntField(DateTimeOffset.UtcNow.ToUnixTimeSeconds())
            };
            _firestore.SetDocument($"players/{name}", fields,
                onSuccess: () =>
                {
                    _nickname = name;
                    PlayerPrefs.SetString(EffectiveNicknameKey(), _nickname);
                    PlayerPrefs.Save();
                    ShowMainMenu();
                },
                onError: error =>
                {
                    confirmBtn.interactable = true;
                    ShowNicknameStatus(statusText, $"등록에 실패했습니다: {error}", isError: true);
                });
        }

        private void ShowNicknameStatus(Text statusText, string message, bool isError)
        {
            statusText.text = message;
            statusText.color = isError ? UITheme.Clay : UITheme.Muted;
            statusText.gameObject.SetActive(true);
        }

        private void BuildMainMenuScreen(Transform root)
        {
            _mainMenuScreen = BuildFullScreenContainer(root, "MainMenuScreen");

            var centered = UiFactory.VerticalGroup(_mainMenuScreen.transform, "Centered", spacing: 14,
                padding: new RectOffset(40, 40, 40, 40));
            centered.anchorMin = new Vector2(0.28f, 0.22f);
            centered.anchorMax = new Vector2(0.72f, 0.78f);
            centered.offsetMin = Vector2.zero;
            centered.offsetMax = Vector2.zero;

            _menuGreetingText = UiFactory.Text(centered, "", 20, UITheme.Ink, TextAnchor.MiddleCenter, FontStyle.Bold);
            UiFactory.Text(centered, "대전 방식을 선택하세요", 13, UITheme.Muted, TextAnchor.MiddleCenter);

            var aiBtn = UiFactory.Button(centered, "AI 매칭", UITheme.Accent, Color.white, StartAiMatch, 16);
            UiFactory.SetHeight(aiBtn, 52);

            var randomBtn = UiFactory.Button(centered, "랜덤 매칭", UITheme.Surface2, UITheme.Ink,
                ShowRandomMatchScreen, 16);
            UiFactory.SetHeight(randomBtn, 52);

            var friendBtn = UiFactory.Button(centered, "친구 매칭", UITheme.Surface2, UITheme.Ink,
                ShowFriendMatchScreen, 16);
            UiFactory.SetHeight(friendBtn, 52);

            var resetBtn = UiFactory.Button(centered, "닉네임 변경", UITheme.Bg, UITheme.Muted, ShowNicknameScreen, 12);
            UiFactory.SetHeight(resetBtn, 30);
        }

        private void BuildRandomMatchScreen(Transform root)
        {
            _randomMatchScreen = BuildFullScreenContainer(root, "RandomMatchScreen");

            var centered = UiFactory.VerticalGroup(_randomMatchScreen.transform, "Centered", spacing: 18,
                padding: new RectOffset(40, 40, 40, 40));
            centered.anchorMin = new Vector2(0.24f, 0.32f);
            centered.anchorMax = new Vector2(0.76f, 0.68f);
            centered.offsetMin = Vector2.zero;
            centered.offsetMax = Vector2.zero;

            _randomMatchStatusText = UiFactory.Text(centered, "매칭 상대를 찾는 중...", 18, UITheme.Ink,
                TextAnchor.MiddleCenter, FontStyle.Bold);

            var cancelBtn = UiFactory.Button(centered, "취소", UITheme.Surface2, UITheme.Ink, () =>
            {
                _matchmaking?.LeaveRandomQueue();
                ShowMainMenu();
            }, 14);
            UiFactory.SetHeight(cancelBtn, 40);
        }

        private void ShowRandomMatchScreen()
        {
            ShowOnly(_randomMatchScreen);
            _headerText.text = "랜덤 매칭";
            _tokenText.text = "";
            _randomMatchStatusText.text = "매칭 상대를 찾는 중...";

            _matchmaking = new MatchmakingService(_firestore, this, _nickname);
            _matchmaking.JoinRandomQueue(
                (opponent, matchId) =>
                {
                    _matchmaking.LeaveRandomQueue();
                    BeginOnlineMatchFlow(matchId, opponent);
                },
                error => _randomMatchStatusText.text = $"오류: {error}");
        }

        private void BuildFriendMatchScreen(Transform root)
        {
            _friendMatchScreen = BuildFullScreenContainer(root, "FriendMatchScreen");

            var centered = UiFactory.VerticalGroup(_friendMatchScreen.transform, "Centered", spacing: 14,
                padding: new RectOffset(40, 40, 40, 40));
            centered.anchorMin = new Vector2(0.26f, 0.24f);
            centered.anchorMax = new Vector2(0.74f, 0.76f);
            centered.offsetMin = Vector2.zero;
            centered.offsetMax = Vector2.zero;

            UiFactory.Text(centered, "친구 매칭", 20, UITheme.Ink, TextAnchor.MiddleCenter, FontStyle.Bold);
            UiFactory.Text(centered, "상대 닉네임을 입력해 초대를 보내세요.\n이 화면에 있는 동안 받은 초대도 자동으로 뜹니다.",
                12, UITheme.Muted, TextAnchor.MiddleCenter);

            _friendTargetInput = UiFactory.InputField(centered, "상대 닉네임");
            _friendStatusText = UiFactory.Text(centered, "", 12, UITheme.Muted, TextAnchor.MiddleCenter);
            _friendStatusText.gameObject.SetActive(false);

            Button inviteBtn = null;
            inviteBtn = UiFactory.Button(centered, "초대 보내기", UITheme.Accent, Color.white, () =>
            {
                string target = _friendTargetInput.text.Trim();
                if (target.Length == 0) { ShowFriendStatus("닉네임을 입력해주세요.", true); return; }
                if (target == _nickname) { ShowFriendStatus("자기 자신은 초대할 수 없습니다.", true); return; }

                inviteBtn.interactable = false;
                ShowFriendStatus("초대를 보내는 중...", false);

                _matchmaking.SendFriendInvite(target,
                    acceptedMatchId =>
                    {
                        inviteBtn.interactable = true;
                        BeginOnlineMatchFlow(acceptedMatchId, target);
                    },
                    () =>
                    {
                        inviteBtn.interactable = true;
                        ShowFriendStatus("상대가 초대를 거절했습니다.", true);
                    },
                    error =>
                    {
                        inviteBtn.interactable = true;
                        ShowFriendStatus(error, true);
                    });
            }, 15);
            UiFactory.SetHeight(inviteBtn, 44);

            var backBtn = UiFactory.Button(centered, "메인 메뉴로", UITheme.Bg, UITheme.Muted, () =>
            {
                _matchmaking?.StopPolling();
                ShowMainMenu();
            }, 12);
            UiFactory.SetHeight(backBtn, 30);
        }

        private void ShowFriendStatus(string message, bool isError)
        {
            _friendStatusText.text = message;
            _friendStatusText.color = isError ? UITheme.Clay : UITheme.Muted;
            _friendStatusText.gameObject.SetActive(true);
        }

        private void ShowFriendMatchScreen()
        {
            ShowOnly(_friendMatchScreen);
            _headerText.text = "친구 매칭";
            _tokenText.text = "";
            _friendTargetInput.text = "";
            _friendStatusText.gameObject.SetActive(false);

            // Incoming invites are watched globally (see StartGlobalInviteWatch,
            // running since the main menu) -- this instance is only for SENDING.
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
                    _choiceOverlay.ShowMixed($"{fromNickname}님이 대전을 신청했습니다.",
                        ("수락", UITheme.Accent, () =>
                        {
                            _inviteWatcher.RespondToInvite(fromNickname, matchId, true,
                                () => BeginOnlineMatchFlow(matchId, fromNickname),
                                error => Debug.LogWarning($"초대 수락 실패: {error}"));
                        }),
                        ("거절", UITheme.Clay, () =>
                        {
                            _inviteWatcher.RespondToInvite(fromNickname, matchId, false,
                                () => { }, error => Debug.LogWarning($"초대 응답 실패: {error}"));
                            StartGlobalInviteWatch(); // resume watching for the next one
                        }));
                },
                error => Debug.LogWarning($"초대 확인 실패: {error}"));
        }

        // Both matchmaking paths land here once paired. One more read of the
        // match doc settles which side (A/B) I am, then the controller takes
        // over turn-by-turn while the existing screens (same CardPickerView,
        // StatusPanelView, ChoiceOverlayView as AI mode) drive the UI -- only
        // the turn-flow methods below source/sink through the network instead
        // of an AiOpponent. Hero selection happens first, same as AI mode.
        private void BeginOnlineMatchFlow(string matchId, string opponentNickname)
        {
            _firestore.GetDocument($"matches/{matchId}", doc =>
            {
                if (doc == null)
                {
                    ShowMainMenu();
                    _choiceOverlay.Show("매칭 정보를 불러오지 못했습니다.", ("확인", () => { }));
                    return;
                }

                string playerA = FirestoreClient.ReadString(doc, "playerA");
                var mySide = playerA == _nickname ? MatchSide.A : MatchSide.B;

                _network = new NetworkMatchController(_firestore, this, matchId, mySide, _nickname, opponentNickname);
                WireNetworkEvents();
                _network.Start();
                _isOnlineMatch = true;
                _state = new GameState();

                ShowHeroSelectScreen();
            }, error =>
            {
                ShowMainMenu();
                _choiceOverlay.Show($"매칭 정보를 불러오지 못했습니다: {error}", ("확인", () => { }));
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
                RefreshTokenHeader();
                UpdateOnlineTurnUI();
            };

            _network.OnOpponentGuess += guess =>
            {
                var trueResult = Judge.Evaluate(_state.PlayerSecret, guess);
                bool canLieAboutWin = _state.PlayerHero == HeroId.Hunter;
                PromptDefenseChoice(guess, trueResult, _state.PlayerLieTokens, canLieAboutWin, (reported, usedToken) =>
                {
                    if (usedToken) { _state.PlayerLieTokens--; RefreshTokenHeader(); }
                    bool wasLie = !reported.Equals(trueResult);
                    _statusPanel.OpponentAttackHistory.AddRow(guess, reported, wasLie);
                    _network.SubmitDefenseResponse(trueResult, reported);
                    _headerText.text = $"{_network.OpponentNickname}님의 판단을 기다리는 중...";
                    _attackPicker.SetInteractable(false);
                });
            };

            _network.OnOpponentResponse += reported =>
            {
                _statusPanel.MyAttackHistory.AddRow(_myLastOnlineGuess, reported, false, revealLie: false);
                PromptTrustOrChallenge(reported, null,
                    onTrust: () =>
                    {
                        _network.SubmitTrust();
                        UpdateOnlineTurnUI();
                    },
                    onChallenge: () =>
                    {
                        _network.SubmitChallenge();
                        _headerText.text = $"{_network.OpponentNickname}님의 응답을 기다리는 중...";
                    });
            };

            _network.OnChallengeNeedsMyJudgement += () => _network.ResolveChallenge(_network.DidIActuallyLie());

            _network.OnIMustReveal += () =>
            {
                PromptPlayerRevealChoice("비밀번호 한 자리를 공개해야 합니다.", index =>
                {
                    if (index < 0) { UpdateOnlineTurnUI(); return; }
                    _network.SubmitReveal(index, _state.PlayerSecret[index]);
                    UpdateOnlineTurnUI();
                });
            };

            _network.OnOpponentRevealed += (side, index, digit) =>
            {
                _state.AiSecret[index] = digit;
                _state.AiRevealed[index] = true;
                RefreshRevealedRows();
            };

            _network.OnTurnChanged += _ => UpdateOnlineTurnUI();

            _network.OnGameOver += winner => EndOnlineGame(winner == _network.MySide);

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
                string text = answer ? "맞습니다" : "아닙니다";
                _choiceOverlay.Show($"상대의 답변: \"{text}\"", ("확인", () => { }));
            };

            // Wizard: opponent used their ability, so I must reveal one of my own
            // still-hidden digits back to them.
            _network.OnMustRevealForWizard += () =>
            {
                int idx = PickRandomOpenIndex(_state.PlayerRevealed);
                if (idx < 0) return;
                _state.PlayerRevealed[idx] = true;
                RefreshRevealedRows();
                _network.SubmitWizardResponse(idx, _state.PlayerSecret[idx]);
            };
        }

        private void UpdateOnlineTurnUI()
        {
            if (_network.CurrentAttacker == _network.MySide) BeginOnlineAttackTurn();
            else BeginOnlineWaitTurn();
        }

        private void BeginOnlineAttackTurn()
        {
            RefreshTokenHeader();
            _headerText.text = $"내 턴 — {_network.OpponentNickname}님의 비밀번호를 추리하세요";
            _attackPicker.SetInteractable(true);
            RefreshPriestButtonVisibility(true);
            _timer.Start(GuessSeconds, () =>
            {
                _attackPicker.SetInteractable(false);
                OnOnlineGuessSubmitted(RandomDigits(false));
            });
        }

        private void BeginOnlineWaitTurn()
        {
            _timer.Stop();
            RefreshTokenHeader();
            _headerText.text = $"{_network.OpponentNickname}님의 턴 — 대기 중";
            _attackPicker.SetInteractable(false);
            RefreshPriestButtonVisibility(false);
        }

        private void OnOnlineGuessSubmitted(int[] guess)
        {
            _timer.Stop();
            _attackPicker.ResetAll();
            _myLastOnlineGuess = guess;
            _attackPicker.SetInteractable(false);
            _headerText.text = $"{_network.OpponentNickname}님의 응답을 기다리는 중...";
            bool keepTurn = _state.PlayerExtraTurnPending;
            _state.PlayerExtraTurnPending = false;
            _network.SubmitGuess(guess, keepTurn);
        }

        private void EndOnlineGame(bool won)
        {
            _timer.Stop();
            _network?.Stop();
            _gameOverText.text = won
                ? "승리! 상대 비밀번호를 정확히 맞혔습니다."
                : $"패배. 내 비밀번호 {string.Join(" ", _state.PlayerSecret)}를 들켰습니다.";
            _gameOverScreen.SetActive(true);
        }

        // ---------- hero select ----------

        private void BuildHeroSelectScreen(Transform root)
        {
            _heroSelectScreen = BuildFullScreenContainer(root, "HeroSelectScreen");

            var centered = UiFactory.VerticalGroup(_heroSelectScreen.transform, "Centered", spacing: 16,
                padding: new RectOffset(30, 30, 24, 24));
            centered.anchorMin = new Vector2(0.05f, 0.08f);
            centered.anchorMax = new Vector2(0.95f, 0.92f);
            centered.offsetMin = Vector2.zero;
            centered.offsetMax = Vector2.zero;

            UiFactory.Text(centered, "영웅을 선택하세요", 20, UITheme.Ink, TextAnchor.MiddleCenter, FontStyle.Bold);

            var grid = UiFactory.Grid(centered, "HeroGrid", columns: 4, cellSize: 150, spacing: 14);
            UiFactory.SetHeight(grid, 150 * 2 + 14); // 7 heroes over 4 columns = 2 rows

            foreach (var info in HeroCatalog.All)
            {
                var heroId = info.Id;
                var cardGo = new GameObject($"Hero_{heroId}", typeof(RectTransform), typeof(Image), typeof(Button));
                cardGo.transform.SetParent(grid, false);
                var img = cardGo.GetComponent<Image>();
                img.color = UITheme.Surface2;
                var btn = cardGo.GetComponent<Button>();
                btn.targetGraphic = img;
                btn.onClick.AddListener(() => OnHeroChosen(heroId));
                _heroSelectButtons.Add(btn);

                var inner = UiFactory.VerticalGroup(cardGo.transform, "Inner", spacing: 4,
                    padding: new RectOffset(6, 6, 10, 8), childAlign: TextAnchor.UpperCenter);
                UiFactory.StretchToFillParent(inner);

                var portraitGo = new GameObject("Portrait", typeof(RectTransform), typeof(Image));
                portraitGo.transform.SetParent(inner, false);
                UiFactory.SetSize(portraitGo.transform, 78, 78);
                var portraitImg = portraitGo.GetComponent<Image>();
                portraitImg.sprite = Resources.Load<Sprite>($"Heroes/Hero_{heroId}_Portrait");
                portraitImg.preserveAspect = true;

                UiFactory.Text(inner, info.Name, 14, UITheme.Ink, TextAnchor.MiddleCenter, FontStyle.Bold);
                UiFactory.Text(inner, info.AbilityName, 10, UITheme.Muted, TextAnchor.MiddleCenter);
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
            _headerText.text = _isOnlineMatch ? $"영웅 선택 — 상대: {_network.OpponentNickname}" : "영웅을 선택하세요";
            _tokenText.text = "";
            _timer.Start(HeroPickSeconds, () => OnHeroChosen(RandomHeroId()));
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
                _headerText.text = "상대의 영웅 선택을 기다리는 중...";
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

            var centered = UiFactory.VerticalGroup(rt, "Centered", spacing: 16,
                padding: new RectOffset(40, 40, 40, 40));
            centered.anchorMin = new Vector2(0.2f, 0.1f);
            centered.anchorMax = new Vector2(0.8f, 0.9f);
            centered.offsetMin = Vector2.zero;
            centered.offsetMax = Vector2.zero;

            UiFactory.Text(centered, "당신의 비밀번호 4자리를 선택하세요", 20, UITheme.Ink, TextAnchor.MiddleCenter, FontStyle.Bold);
            UiFactory.Text(centered, "0~9 중 중복 없는 숫자 4개. 상대는 이 번호를 볼 수 없습니다.", 13, UITheme.Muted, TextAnchor.MiddleCenter);

            _setupPicker = new CardPickerView(centered, "내 비밀번호", "게임 시작", OnSecretChosen);
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
                ? "비밀번호 준비 — 상대 능력으로 9는 사용할 수 없습니다"
                : "비밀번호 준비";
            _tokenText.text = "";

            _timer.Start(SecretPickSeconds, () =>
            {
                _setupPicker.SetInteractable(false);
                OnSecretChosen(RandomDigits(opponentIsDemonHunter));
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

            var outer = UiFactory.VerticalGroup(rt, "Outer", spacing: 0);
            outer.anchorMin = Vector2.zero;
            outer.anchorMax = Vector2.one;
            outer.offsetMin = Vector2.zero;
            outer.offsetMax = Vector2.zero;

            var heroBar = UiFactory.HorizontalGroup(outer, "HeroBar", spacing: 0,
                padding: new RectOffset(0, 0, 6, 6), childAlign: TextAnchor.MiddleCenter);
            heroBar.gameObject.AddComponent<Image>().color = UITheme.Surface;
            UiFactory.SetHeight(heroBar, 96);

            var oppSide = UiFactory.HorizontalGroup(heroBar, "OppSide", spacing: 0, childAlign: TextAnchor.MiddleCenter);
            UiFactory.SetFlexible(oppSide, 1, 1);
            _opponentHeroPortrait = new HeroPortraitView(oppSide, showCharges: false);

            var vsText = UiFactory.Text(heroBar, "VS", 20, UITheme.Muted, TextAnchor.MiddleCenter, FontStyle.Bold);
            UiFactory.SetSize(vsText, 50, 40);

            var mySide = UiFactory.HorizontalGroup(heroBar, "MySide", spacing: 0, childAlign: TextAnchor.MiddleCenter);
            UiFactory.SetFlexible(mySide, 1, 1);
            _myHeroPortrait = new HeroPortraitView(mySide, showCharges: true);
            _myHeroPortrait.SetAbilityClickable(OnMyAbilityClicked);

            var body = UiFactory.HorizontalGroup(outer, "Body", spacing: 0);
            UiFactory.SetFlexible(body, 1, 1);

            var left = UiFactory.Panel(body, "LeftPane", UITheme.Surface);
            UiFactory.SetFlexible(left, 1, 1);
            _statusPanel = new StatusPanelView(left, this);

            var right = UiFactory.Panel(body, "RightPane", UITheme.Bg);
            UiFactory.SetFlexible(right, 1, 1);
            var rightInner = UiFactory.VerticalGroup(right, "RightInner", spacing: 12,
                padding: new RectOffset(18, 18, 18, 18));
            UiFactory.StretchToFillParent(rightInner);

            _attackPicker = new CardPickerView(rightInner, "내 추리 — 숫자 카드", "추측 제출", OnPlayerGuessSubmitted);

            _priestButton = UiFactory.Button(rightInner, "사제 능력: 한 자리 묻기", UITheme.Clay, Color.white,
                ShowPriestPositionPicker, 13);
            UiFactory.SetHeight(_priestButton, 36);
            _priestButton.gameObject.SetActive(false);

            _matchScreen.SetActive(false);
        }

        // Ability-icon click: only the "always available, single click" heroes
        // route through here. Rogue asks during the trust/challenge decision,
        // Priest replaces a normal guess via its own dedicated button -- both
        // have their own entry points below instead.
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
            _state.PlayerAbilityCharges--;
            _state.PlayerLieTokens = Mathf.Min(_state.PlayerLieTokens + 1, 2);
            RefreshTokenHeader();
            RefreshMyAbilityUI();
        }

        // Arms a flag consumed the next time I actually end my attack turn (see
        // EndMyAttackTurnAndPassToOpponent / the online SubmitGuess keepTurn
        // param) -- clicking it isn't itself turn-scoped, so it's safe to press
        // any time and it just pre-arms my next attack.
        private void UseWarriorAbility()
        {
            _state.PlayerAbilityCharges--;
            _state.PlayerExtraTurnPending = true;
            RefreshMyAbilityUI();
        }

        private void UseWizardAbility()
        {
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
        }

        private void RefreshMyAbilityUI()
        {
            if (_state == null) return;
            var info = HeroCatalog.Get(_state.PlayerHero);
            _myHeroPortrait.RefreshCharges(_state.PlayerAbilityCharges, info.Charges);
            RefreshAbilityIconClickability();
        }

        private static int PickRandomOpenIndex(bool[] revealed)
        {
            var open = new List<int>();
            for (int i = 0; i < revealed.Length; i++)
                if (!revealed[i]) open.Add(i);
            return open.Count > 0 ? open[UnityEngine.Random.Range(0, open.Count)] : -1;
        }

        // Priest's query button lives next to the attack picker (not on the hero
        // portrait) since it's an ALTERNATIVE to submitting a normal guess this
        // turn, not a free side action.
        private void RefreshPriestButtonVisibility(bool myTurnActive)
        {
            bool eligible = _state != null && _state.PlayerHero == HeroId.Priest && _state.PlayerAbilityCharges > 0;
            _priestButton.gameObject.SetActive(eligible);
            _priestButton.interactable = eligible && myTurnActive;
        }

        private void ShowPriestPositionPicker()
        {
            var buttons = new (string, Action)[4];
            for (int i = 0; i < 4; i++)
            {
                int pos = i;
                buttons[i] = ($"{pos + 1}번째 자리", () => ShowPriestDigitGuessPicker(pos));
            }
            _choiceOverlay.Show("어느 자리를 물어볼까요?", buttons);
        }

        // Second step: guess a specific digit for that position ("2번째 자리
        // 숫자 5지?") instead of asking an open "what's the digit" question.
        private void ShowPriestDigitGuessPicker(int position)
        {
            var options = new List<(string, Action)>();
            for (int d = 0; d <= 9; d++)
            {
                int digit = d;
                options.Add((digit.ToString(), () => OnPriestQuerySubmitted(position, digit)));
            }
            _choiceOverlay.Show($"{position + 1}번째 자리, 어떤 숫자일지 물어볼까요?", options.ToArray());
        }

        private void OnPriestQuerySubmitted(int position, int guessedDigit)
        {
            _timer.Stop();
            _state.PlayerAbilityCharges--;
            RefreshMyAbilityUI();
            RefreshPriestButtonVisibility(false);
            _attackPicker.SetInteractable(false);
            _attackPicker.ResetAll();

            if (_isOnlineMatch)
            {
                _headerText.text = $"{_network.OpponentNickname}님의 답변을 기다리는 중...";
                _network.SubmitPriestQuery(position, guessedDigit);
            }
            else
            {
                bool answer = _state.AiSecret[position] == guessedDigit;
                string answerText = answer ? "맞습니다" : "아닙니다";
                _choiceOverlay.Show($"상대의 답변: {position + 1}번째 자리가 {guessedDigit}인지에 대해 \"{answerText}\"",
                    ("확인", EndMyAttackTurnAndPassToOpponent));
            }
        }

        // Shared by AI mode (AI asks, I answer) and online mode (opponent asks,
        // I answer). Priest's query always gets a truthful answer -- no LIE
        // TOKEN option here, unlike a normal guess's strike/ball report.
        private void PromptPriestDefenseChoice(int position, int guessedDigit, Action<bool> onAnswered)
        {
            bool trueAnswer = _state.PlayerSecret[position] == guessedDigit;
            string truthLabel = trueAnswer ? "예" : "아니오";
            string message = $"상대가 묻습니다: {position + 1}번째 자리가 {guessedDigit}인가요?\n진짜 답: {truthLabel}";
            _choiceOverlay.Show(message, ("확인", () => onAnswered(trueAnswer)));
        }

        private void BuildGameOverScreen(Transform root)
        {
            _gameOverScreen = new GameObject("GameOverScreen", typeof(RectTransform), typeof(Image));
            var rt = (RectTransform)_gameOverScreen.transform;
            rt.SetParent(root, false);
            rt.anchorMin = Vector2.zero;
            rt.anchorMax = Vector2.one;
            rt.offsetMin = Vector2.zero;
            rt.offsetMax = Vector2.zero;
            _gameOverScreen.GetComponent<Image>().color = new Color(0, 0, 0, 0.75f);

            var box = UiFactory.VerticalGroup(rt, "Box", spacing: 18, padding: new RectOffset(40, 40, 40, 40));
            box.anchorMin = new Vector2(0.3f, 0.35f);
            box.anchorMax = new Vector2(0.7f, 0.65f);
            box.offsetMin = Vector2.zero;
            box.offsetMax = Vector2.zero;
            box.gameObject.AddComponent<Image>().color = UITheme.Bg;

            _gameOverText = UiFactory.Text(box, "", 24, UITheme.Ink, TextAnchor.MiddleCenter, FontStyle.Bold);
            UiFactory.Button(box, "메인 메뉴로", UITheme.Accent, Color.white, () =>
            {
                _timer.Stop();
                _network?.Stop();
                _network = null;
                _isOnlineMatch = false;
                _gameOverScreen.SetActive(false);
                ShowMainMenu();
            }, 16);

            _gameOverScreen.SetActive(false);
        }

        // ---------- flow ----------

        // The top-level screens are mutually exclusive; overlays (game over, the
        // choice modal) toggle independently on top of whichever is active.
        private void ShowOnly(GameObject target)
        {
            _nicknameScreen.SetActive(target == _nicknameScreen);
            _mainMenuScreen.SetActive(target == _mainMenuScreen);
            _randomMatchScreen.SetActive(target == _randomMatchScreen);
            _friendMatchScreen.SetActive(target == _friendMatchScreen);
            _heroSelectScreen.SetActive(target == _heroSelectScreen);
            _setupScreen.SetActive(target == _setupScreen);
            _matchScreen.SetActive(target == _matchScreen);
        }

        private void ShowNicknameScreen()
        {
            ShowOnly(_nicknameScreen);
            _headerText.text = "라이어스 배팅";
            _tokenText.text = "";
        }

        private void ShowMainMenu()
        {
            _timer.Stop();
            ShowOnly(_mainMenuScreen);
            _menuGreetingText.text = $"{_nickname}님, 환영합니다";
            _headerText.text = "라이어스 배팅";
            _tokenText.text = "";
            StartGlobalInviteWatch();
        }

        // AI-mode entry point (the "AI 매칭" button). Picks the AI's hero and
        // (if it's DemonHunter) restricts its own guessing model right away,
        // then hands off to the shared hero-select screen.
        private void StartAiMatch()
        {
            _isOnlineMatch = false;
            _inviteWatcher?.StopPolling();

            _state = new GameState();
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

            ShowOnly(_matchScreen);
            _myHeroPortrait.SetHero(_state.PlayerHero, _state.PlayerAbilityCharges);
            _opponentHeroPortrait.SetHero(_state.AiHero);
            RefreshAbilityIconClickability();
            _statusPanel.ShowMySecret(playerSecret);
            _statusPanel.OpponentAttackHistory.Clear();
            _statusPanel.MyAttackHistory.Clear();
            RefreshRevealedRows();

            bool aiFirst = UnityEngine.Random.value > 0.5f;
            if (aiFirst) BeginAiAttackTurn();
            else BeginPlayerAttackTurn();
        }

        // Rogue and Priest don't use the ability-icon click at all (Rogue offers
        // its button inside the trust/challenge prompt, Priest gets its own
        // button next to the attack picker) -- so the icon shouldn't look
        // clickable for them even while they have charges left.
        private void RefreshAbilityIconClickability()
        {
            bool iconDriven = _state.PlayerHero != HeroId.Rogue && _state.PlayerHero != HeroId.Priest;
            _myHeroPortrait.SetAbilityInteractable(iconDriven && _state.PlayerAbilityCharges > 0);
        }

        private void OnOnlineSecretChosen(int[] playerSecret)
        {
            _state.PlayerSecret = playerSecret;
            _state.AiSecret = new int[4]; // opponent's secret is never known locally; filled in one digit at a time by reveals

            ShowOnly(_matchScreen);
            _myHeroPortrait.SetHero(_state.PlayerHero, _state.PlayerAbilityCharges);
            _opponentHeroPortrait.SetHero(_state.AiHero);
            RefreshAbilityIconClickability();
            _statusPanel.ShowMySecret(playerSecret);
            _statusPanel.OpponentAttackHistory.Clear();
            _statusPanel.MyAttackHistory.Clear();
            RefreshRevealedRows();

            _attackPicker.SetInteractable(false);
            _headerText.text = $"{_network.OpponentNickname}님이 비밀번호를 정하는 중...";
            _tokenText.text = "";

            _network.MarkMySecretReady();
        }

        private void RefreshTokenHeader()
        {
            // The opponent's remaining LIE TOKEN count is exactly the kind of
            // information the game is designed to hide -- only my own is shown.
            _tokenText.text = $"내 LIE TOKEN {_state.PlayerLieTokens}/2";
        }

        private void BeginPlayerAttackTurn()
        {
            RefreshTokenHeader();
            _headerText.text = "내 턴 — 상대 비밀번호를 추리하세요";
            _attackPicker.SetInteractable(true);
            RefreshPriestButtonVisibility(true);
            _timer.Start(GuessSeconds, () =>
            {
                _attackPicker.SetInteractable(false);
                OnPlayerGuessSubmitted(RandomDigits(false));
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
                BeginPlayerAttackTurn();
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
                BeginAiAttackTurn();
            }
            else
            {
                BeginPlayerAttackTurn();
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

        private void OnPlayerGuessSubmitted(int[] guess)
        {
            _timer.Stop();
            _attackPicker.ResetAll();

            if (_isOnlineMatch) { OnOnlineGuessSubmitted(guess); return; }

            var trueResult = Judge.Evaluate(_state.AiSecret, guess);
            bool canLieAboutWin = _state.AiHero == HeroId.Hunter;

            if (_ai.ShouldUsePaladinHeal(_state.AiLieTokens, _state.AiAbilityCharges) && _state.AiHero == HeroId.Paladin)
            {
                _state.AiAbilityCharges--;
                _state.AiLieTokens = Mathf.Min(_state.AiLieTokens + 1, 2);
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
                EndGame(true);
                return;
            }

            PromptTrustOrChallenge(reported, aiLied,
                onTrust: EndMyAttackTurnAndPassToOpponent,
                onChallenge: () =>
                {
                    if (aiLied)
                    {
                        // Caught it: the AI has to open one of its own digits.
                        RevealAiDigit(prefix: "적중! 상대가 거짓말을 했습니다.", onDone: EndMyAttackTurnAndPassToOpponent);
                    }
                    else
                    {
                        // Wrong accusation: the player opens one of their own digits.
                        PromptPlayerRevealChoice("헛다리! 상대는 진실을 말했습니다. 내 비밀번호 한 자리를 공개하세요.",
                            _ => EndMyAttackTurnAndPassToOpponent());
                    }
                });
        }

        private void BeginAiAttackTurn()
        {
            RefreshTokenHeader();
            _headerText.text = "상대 턴 — 왼쪽에서 방어 결과를 확인하세요";
            _attackPicker.SetInteractable(false);
            RefreshPriestButtonVisibility(false);

            if (_state.AiHero == HeroId.Wizard && _ai.ShouldUseWizardReveal(_state.AiAbilityCharges))
                AiUseWizard();

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
                    _ai.NarrowByOwnGuess(guess, reported);
                    EndAiAttackTurnAndPassToPlayer();
                    return;
                }

                if (playerLied)
                {
                    // Caught: the reported value is known-false, so there's nothing
                    // honest to narrow the AI's model by this turn -- it only gains
                    // the digit reveal, not extra information.
                    PromptPlayerRevealChoice("상대가 당신의 거짓말을 눈치챘습니다! 내 비밀번호 한 자리를 공개하세요.",
                        _ => EndAiAttackTurnAndPassToPlayer());
                }
                else
                {
                    // Wrong accusation: the AI opens one of its own digits, but the
                    // (confirmed-true) result is still good information to narrow by.
                    _ai.NarrowByOwnGuess(guess, reported);
                    RevealAiDigit(prefix: "상대가 당신을 의심했지만 틀렸습니다!", onDone: EndAiAttackTurnAndPassToPlayer);
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

        // Shared by both directions: forces open one of the AI's own digits and
        // shows the result before continuing.
        private void RevealAiDigit(string prefix, Action onDone)
        {
            int idx = _ai.PickRevealIndex(_state.AiRevealed);
            if (idx < 0)
            {
                _choiceOverlay.Show($"{prefix} (이미 모든 자리가 공개됨)", ("확인", onDone));
                return;
            }
            _state.AiRevealed[idx] = true;
            RefreshRevealedRows();
            _choiceOverlay.Show($"{prefix} 상대 비밀번호 {idx + 1}번째 자리 공개: {_state.AiSecret[idx]}",
                ("확인", onDone));
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
                _choiceOverlay.Show($"{message} (이미 모든 자리가 공개됨)", ("확인", () => onRevealed(-1)));
                return;
            }

            var options = new (string, Action)[open.Count];
            for (int n = 0; n < open.Count; n++)
            {
                int i = open[n];
                options[n] = ($"{i + 1}번째: {_state.PlayerSecret[i]}", () =>
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
        }

        // Shows the true result of the opponent's guess (computed against my real
        // secret) and lets me choose to report it truthfully or spend a LIE TOKEN.
        // A true 4-strike normally can't be lied about -- Hunter is the exception.
        // Owns its own 10s timer: defaults to reporting the truth on timeout.
        private void PromptDefenseChoice(int[] opponentGuess, JudgeResult trueResult, int lieTokensLeft,
            bool canLieAboutWin, Action<JudgeResult, bool> onChoice)
        {
            void Resolve(JudgeResult result, bool usedToken)
            {
                _timer.Stop();
                onChoice(result, usedToken);
            }

            string resultText = trueResult.IsOut ? "OUT" : $"{trueResult.Strike}S {trueResult.Ball}B";
            string message = $"상대 추측: {string.Join(" ", opponentGuess)}\n진짜 결과: {resultText}";

            bool canLie = lieTokensLeft > 0 && (!trueResult.IsWin || canLieAboutWin);
            if (!canLie)
            {
                _choiceOverlay.Show(message, ("진실대로 전송", () => Resolve(trueResult, false)));
            }
            else
            {
                _choiceOverlay.ShowMixed(message,
                    ("진실대로 전송", UITheme.Accent, () => Resolve(trueResult, false)),
                    ($"LIE TOKEN 사용 ({lieTokensLeft}개)", UITheme.Clay,
                        () => PromptFakeStrikeChoice(trueResult, (r, u) => Resolve(r, u))));
            }

            _timer.Start(DefendChoiceSeconds, () => Resolve(trueResult, false));
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
                buttons[i] = ($"{s} 스트라이크", UITheme.StrikeFg, () => PromptFakeBallChoice(s, allFakes, onChoice));
            }
            _choiceOverlay.ShowMixed("거짓말: 스트라이크를 몇 개로 알려줄까요?", buttons);
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
                string label = fake.IsOut ? "OUT" : $"{fake.Ball} 볼";
                buttons[i] = (label, UITheme.BallFg, () => onChoice(fake, true));
            }
            _choiceOverlay.ShowMixed($"거짓말: {strike} 스트라이크로 정했습니다. 볼은 몇 개로 알려줄까요?", buttons);
        }

        // Shared trust/challenge decision (point 4). Owns its own 10s timer:
        // defaults to trusting the report on timeout.
        // knownLieStatusForRogue: AI mode already knows the answer locally (pass
        // it directly); online mode passes null and the Rogue button triggers a
        // _network.SubmitRogueQuery() round-trip instead.
        private void PromptTrustOrChallenge(JudgeResult reported, bool? knownLieStatusForRogue,
            Action onTrust, Action onChallenge)
        {
            void ResolveTrust() { _timer.Stop(); onTrust(); }
            void ResolveChallenge() { _timer.Stop(); onChallenge(); }

            string resultText = reported.IsOut ? "OUT" : $"{reported.Strike}S {reported.Ball}B";
            string message = $"상대가 알려준 결과: {resultText}\n믿으시겠습니까, 거짓말 같습니까?";
            bool rogueAvailable = _state.PlayerHero == HeroId.Rogue && _state.PlayerAbilityCharges > 0;

            var options = new List<(string, Color, Action)>
            {
                ("믿는다", UITheme.Accent, (Action)ResolveTrust),
                ("거짓말 같다 (의심)", UITheme.Clay, (Action)ResolveChallenge)
            };

            if (rogueAvailable)
            {
                // Rogue already knows the answer for certain, so there's nothing
                // left to decide -- using it just tells you the truth and acts on
                // it immediately (challenge if it was a lie, trust if it wasn't),
                // instead of asking you to pick again after being told the answer.
                options.Add(($"진실 간파 ({_state.PlayerAbilityCharges}회)", UITheme.StrikeFg, (Action)(() =>
                {
                    _timer.Stop();
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
                            ShowRogueResult(wasLie);
                        };
                        _network.OnRogueAnswer += handler;
                        _network.SubmitRogueQuery();
                    }
                })));
            }

            void ShowRogueResult(bool wasLie)
            {
                string resultMessage = wasLie ? "진실 간파 결과: 거짓말이었습니다!" : "진실 간파 결과: 진실이었습니다!";
                _choiceOverlay.Show(resultMessage, ("확인", wasLie ? (Action)ResolveChallenge : ResolveTrust));
            }

            System.Collections.IEnumerator ShowRogueResultNextFrame(bool wasLie)
            {
                yield return null;
                ShowRogueResult(wasLie);
            }

            _choiceOverlay.ShowMixed(message, options.ToArray());
            _timer.Start(TrustChoiceSeconds, ResolveTrust);
        }

        private void EndGame(bool playerWon)
        {
            _timer.Stop();
            _gameOverText.text = playerWon
                ? $"승리! 상대 비밀번호는 {string.Join(" ", _state.AiSecret)} 였습니다."
                : $"패배. 내 비밀번호 {string.Join(" ", _state.PlayerSecret)}를 들켰습니다.";
            _gameOverScreen.SetActive(true);
        }
    }
}
