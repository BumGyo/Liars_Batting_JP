using System;
using System.Collections;
using Newtonsoft.Json.Linq;
using UnityEngine;

namespace LiarsBatting.Network
{
    // Drives random-queue and friend-invite matchmaking over Firestore with
    // plain polling -- no Cloud Functions. Two clients can see the same queue
    // snapshot and both try to create a match at once, so every decision here
    // is a deterministic rule (smallest nickname wins) that both sides compute
    // identically, rather than a race one side has to win.
    public class MatchmakingService
    {
        private const float PollIntervalSeconds = 2f;

        // A ticket nobody has refreshed in this long is treated as abandoned (a
        // crashed client, a closed window that skipped LeaveRandomQueue) and
        // ignored -- otherwise one leftover ticket from an earlier test run
        // could permanently block matching, since it might always "win" the
        // smallest-nickname race without ever being polled to finish the job.
        private const long StaleTicketSeconds = 20;

        private readonly FirestoreClient _db;
        private readonly MonoBehaviour _host;
        private readonly string _myNickname;
        private Coroutine _pollRoutine;

        public MatchmakingService(FirestoreClient db, MonoBehaviour host, string myNickname)
        {
            _db = db;
            _host = host;
            _myNickname = myNickname;
        }

        public void StopPolling()
        {
            if (_pollRoutine != null)
            {
                _host.StopCoroutine(_pollRoutine);
                _pollRoutine = null;
            }
        }

        private void StartPolling(Action pollOnce)
        {
            StopPolling();
            pollOnce(); // don't wait a full interval before the first attempt
            _pollRoutine = _host.StartCoroutine(PollLoop(pollOnce));
        }

        private IEnumerator PollLoop(Action pollOnce)
        {
            while (true)
            {
                yield return new WaitForSeconds(PollIntervalSeconds);
                pollOnce();
            }
        }

        // ---------- random queue ----------

        public void JoinRandomQueue(Action<string, string> onMatched, Action<string> onError)
        {
            var fields = new JObject
            {
                ["nickname"] = FirestoreClient.StringField(_myNickname),
                ["joinedAt"] = FirestoreClient.IntField(DateTimeOffset.UtcNow.ToUnixTimeSeconds()),
                ["matchId"] = FirestoreClient.StringField("")
            };
            _db.SetDocument($"matchQueue/{_myNickname}", fields,
                onSuccess: () => StartPolling(() => PollRandomQueueOnce(onMatched, onError)),
                onError: onError);
        }

        public void LeaveRandomQueue()
        {
            StopPolling();
            _db.DeleteDocument($"matchQueue/{_myNickname}", null, null);
        }

        private void PollRandomQueueOnce(Action<string, string> onMatched, Action<string> onError)
        {
            _db.GetDocument($"matchQueue/{_myNickname}", myDoc =>
            {
                if (myDoc == null) return; // left the queue from elsewhere

                string myMatchId = FirestoreClient.ReadString(myDoc, "matchId", "");
                if (!string.IsNullOrEmpty(myMatchId))
                {
                    _db.GetDocument($"matches/{myMatchId}", matchDoc =>
                    {
                        if (matchDoc == null) return;
                        string a = FirestoreClient.ReadString(matchDoc, "playerA");
                        string b = FirestoreClient.ReadString(matchDoc, "playerB");
                        string opponent = a == _myNickname ? b : a;
                        StopPolling();
                        onMatched(opponent, myMatchId);
                    }, onError);
                    return;
                }

                // Heartbeat: refresh my own joinedAt so nobody else's staleness
                // check mistakes me for an abandoned ticket while I'm still here.
                long now = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
                _db.SetDocument($"matchQueue/{_myNickname}",
                    new JObject
                    {
                        ["nickname"] = FirestoreClient.StringField(_myNickname),
                        ["joinedAt"] = FirestoreClient.IntField(now),
                        ["matchId"] = FirestoreClient.StringField("")
                    }, null, null);

                // Still waiting -- look for anyone else free, and only act if I'm
                // the one both sides would agree should create the match.
                _db.ListCollection("matchQueue", docs =>
                {
                    string bestCandidate = null;
                    JObject bestCandidateDoc = null;
                    bool iAmSmallestAvailable = true;

                    foreach (var token in docs)
                    {
                        var doc = (JObject)token;
                        string nickname = FirestoreClient.ReadDocumentId(doc);
                        if (string.IsNullOrEmpty(nickname) || nickname == _myNickname) continue;
                        string matchId = FirestoreClient.ReadString(doc, "matchId", "");
                        if (!string.IsNullOrEmpty(matchId)) continue; // already paired up
                        long joinedAt = FirestoreClient.ReadInt(doc, "joinedAt", 0);
                        if (now - joinedAt > StaleTicketSeconds) continue; // abandoned -- ignore

                        if (string.CompareOrdinal(nickname, _myNickname) < 0)
                            iAmSmallestAvailable = false;
                        if (bestCandidate == null || string.CompareOrdinal(nickname, bestCandidate) < 0)
                        {
                            bestCandidate = nickname;
                            bestCandidateDoc = doc;
                        }
                    }

                    if (bestCandidate == null || !iAmSmallestAvailable) return;

                    string newMatchId = Guid.NewGuid().ToString("N");
                    var matchFields = new JObject
                    {
                        ["playerA"] = FirestoreClient.StringField(_myNickname),
                        ["playerB"] = FirestoreClient.StringField(bestCandidate),
                        ["status"] = FirestoreClient.StringField("matched")
                    };
                    _db.SetDocument($"matches/{newMatchId}", matchFields, () =>
                    {
                        _db.SetDocument($"matchQueue/{_myNickname}",
                            WithMatchId(myDoc, _myNickname, newMatchId), null, null);
                        _db.SetDocument($"matchQueue/{bestCandidate}",
                            WithMatchId(bestCandidateDoc, bestCandidate, newMatchId), null, null);
                    }, onError);
                }, onError);
            }, onError);
        }

        private static JObject WithMatchId(JObject originalDoc, string nickname, string matchId)
        {
            long joinedAt = FirestoreClient.ReadInt(originalDoc, "joinedAt", 0);
            return new JObject
            {
                ["nickname"] = FirestoreClient.StringField(nickname),
                ["joinedAt"] = FirestoreClient.IntField(joinedAt),
                ["matchId"] = FirestoreClient.StringField(matchId)
            };
        }

        // ---------- friend invite ----------

        public void SendFriendInvite(string targetNickname,
            Action<string> onAccepted, Action onDeclined, Action<string> onError)
        {
            _db.GetDocument($"players/{targetNickname}", targetDoc =>
            {
                if (targetDoc == null)
                {
                    onError("존재하지 않는 닉네임입니다.");
                    return;
                }

                string matchId = Guid.NewGuid().ToString("N");
                var matchFields = new JObject
                {
                    ["playerA"] = FirestoreClient.StringField(_myNickname),
                    ["playerB"] = FirestoreClient.StringField(targetNickname),
                    ["status"] = FirestoreClient.StringField("invited")
                };
                _db.SetDocument($"matches/{matchId}", matchFields, () =>
                {
                    var inviteFields = new JObject
                    {
                        ["fromNickname"] = FirestoreClient.StringField(_myNickname),
                        ["matchId"] = FirestoreClient.StringField(matchId),
                        ["status"] = FirestoreClient.StringField("pending")
                    };
                    _db.SetDocument($"invites/{targetNickname}", inviteFields,
                        () => StartPolling(() => PollInviteResponseOnce(matchId, onAccepted, onDeclined, onError)),
                        onError);
                }, onError);
            }, onError);
        }

        private void PollInviteResponseOnce(string matchId,
            Action<string> onAccepted, Action onDeclined, Action<string> onError)
        {
            _db.GetDocument($"matches/{matchId}", doc =>
            {
                if (doc == null) return;
                string status = FirestoreClient.ReadString(doc, "status", "");
                if (status == "waiting_secrets")
                {
                    StopPolling();
                    onAccepted(matchId);
                }
                else if (status == "declined")
                {
                    StopPolling();
                    onDeclined();
                }
            }, onError);
        }

        // Watches my OWN inbox for someone else's invite. Meant to run only
        // while the player is sitting on the friend-match screen.
        public void WatchForIncomingInvite(Action<string, string> onInvite, Action<string> onError)
        {
            StartPolling(() =>
            {
                _db.GetDocument($"invites/{_myNickname}", doc =>
                {
                    if (doc == null) return;
                    if (FirestoreClient.ReadString(doc, "status", "") != "pending") return;

                    string from = FirestoreClient.ReadString(doc, "fromNickname", "");
                    string matchId = FirestoreClient.ReadString(doc, "matchId", "");
                    StopPolling();
                    onInvite(from, matchId);
                }, onError);
            });
        }

        public void RespondToInvite(string fromNickname, string matchId, bool accept,
            Action onDone, Action<string> onError)
        {
            var inviteFields = new JObject
            {
                ["fromNickname"] = FirestoreClient.StringField(fromNickname),
                ["matchId"] = FirestoreClient.StringField(matchId),
                ["status"] = FirestoreClient.StringField(accept ? "accepted" : "declined_by_recipient")
            };
            _db.SetDocument($"invites/{_myNickname}", inviteFields, () =>
            {
                var matchFields = new JObject
                {
                    ["playerA"] = FirestoreClient.StringField(fromNickname),
                    ["playerB"] = FirestoreClient.StringField(_myNickname),
                    ["status"] = FirestoreClient.StringField(accept ? "waiting_secrets" : "declined")
                };
                _db.SetDocument($"matches/{matchId}", matchFields, onDone, onError);
            }, onError);
        }
    }
}
