using System;
using LiarsBatting.Core;
using LiarsBatting.Progression;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using UnityEngine;

namespace LiarsBatting.Network
{
    // Reads and writes the signed-in player's own document, players/{nickname}. Only the owner's
    // client ever writes it, so a cached copy (Current) plus field-level patches is safe: two
    // writes from this one client can't race each other into a lost update.
    //
    // Fields: nickname, lastSeen (existing), profileIcon (HeroId name), wins, losses,
    // level, title (equipped title text, for other players), progress (PlayerProgress as JSON).
    // Older documents simply lack the new fields and read back as the defaults.
    public class ProfileStore
    {
        private const string Collection = "players";

        private readonly FirestoreClient _db;

        public PlayerProfile Current { get; private set; }

        public ProfileStore(FirestoreClient db)
        {
            _db = db;
        }

        private static string PathOf(string nickname) => $"{Collection}/{nickname}";

        // The nickname is the document id, so it can't contain '/' or be one of Firestore's
        // reserved ids. Returns an error message, or null when the name is usable.
        public static string NicknameProblem(string name)
        {
            if (string.IsNullOrWhiteSpace(name)) return "ニックネームを入力してください。";
            if (name.Contains("/") || name == "." || name == ".." || (name.StartsWith("__") && name.EndsWith("__")))
                return "ニックネームに使用できない文字が含まれています。（'/', '.', '..' など）";
            return null;
        }

        // First registration: create players/{nickname} only if nobody has it yet.
        public void Register(string nickname, Action onRegistered, Action<string> onError)
        {
            var fields = new JObject
            {
                ["nickname"] = FirestoreClient.StringField(nickname),
                ["lastSeen"] = FirestoreClient.IntField(DateTimeOffset.UtcNow.ToUnixTimeSeconds())
            };
            _db.CreateDocument(Collection, nickname, fields, onRegistered,
                () => onError?.Invoke("すでに使用されているニックネームです。"),
                error => onError?.Invoke($"登録に失敗しました: {error}"));
        }

        private static PlayerProfile Parse(string nickname, JObject doc)
        {
            var profile = new PlayerProfile { Nickname = nickname };
            if (doc == null) return profile;

            string icon = FirestoreClient.ReadString(doc, "profileIcon", "");
            if (Enum.TryParse(icon, out HeroId hero)) profile.ProfileIcon = hero;
            profile.Wins = (int)FirestoreClient.ReadInt(doc, "wins", 0);
            profile.Losses = (int)FirestoreClient.ReadInt(doc, "losses", 0);

            string json = FirestoreClient.ReadString(doc, "progress", "");
            if (!string.IsNullOrEmpty(json))
            {
                try { profile.Progress = JsonConvert.DeserializeObject<PlayerProgress>(json) ?? new PlayerProgress(); }
                catch (Exception e) { Debug.LogWarning($"進行状況の読み込みに失敗しました。新しく開始します: {e.Message}"); }
            }
            return profile;
        }

        public void Load(string nickname, Action<PlayerProfile> onLoaded, Action<string> onError)
        {
            _db.GetDocument(PathOf(nickname), doc =>
            {
                Current = Parse(nickname, doc);
                onLoaded?.Invoke(Current);
            }, onError);
        }

        // Someone else's profile (e.g. the online opponent's title). Never replaces Current.
        public void Fetch(string nickname, Action<PlayerProfile> onLoaded, Action<string> onError)
        {
            _db.GetDocument(PathOf(nickname), doc => onLoaded?.Invoke(Parse(nickname, doc)), onError);
        }

        // Runs `action` against Current, loading it first if it isn't this player's yet.
        private void WithCurrent(string nickname, Action<PlayerProfile> action, Action<string> onError)
        {
            if (Current != null && Current.Nickname == nickname) { action(Current); return; }
            Load(nickname, action, onError);
        }

        public void SaveProfileIcon(string nickname, HeroId icon, Action onSaved, Action<string> onError)
        {
            WithCurrent(nickname, profile =>
            {
                var fields = new JObject { ["profileIcon"] = FirestoreClient.StringField(icon.ToString()) };
                _db.PatchFields(PathOf(nickname), fields, new[] { "profileIcon" }, () =>
                {
                    profile.ProfileIcon = icon;
                    onSaved?.Invoke();
                }, onError);
            }, onError);
        }

        private static JObject ProgressFields(PlayerProfile profile) => new JObject
        {
            ["progress"] = FirestoreClient.StringField(JsonConvert.SerializeObject(profile.Progress)),
            ["level"] = FirestoreClient.IntField(profile.Progress.Level),
            ["title"] = FirestoreClient.StringField(RewardCatalog.TitleText(profile.Progress.Title))
        };

        private static readonly string[] ProgressFieldPaths = { "progress", "level", "title" };

        // A finished match: XP, level, challenges, and (online only) the record against people.
        public void SettleMatch(string nickname, MatchStats stats, Action<MatchSettlement> onSettled)
        {
            WithCurrent(nickname, profile =>
            {
                if (stats.Online)
                {
                    if (stats.Won) profile.Wins++; else profile.Losses++;
                }
                // null when an early surrender earns nothing (see ProgressionService.EarnsProgress).
                var settlement = ProgressionService.EarnsProgress(stats)
                    ? ProgressionService.ApplyMatch(profile.Progress, stats, DateTime.UtcNow)
                    : null;

                var fields = ProgressFields(profile);
                fields["wins"] = FirestoreClient.IntField(profile.Wins);
                fields["losses"] = FirestoreClient.IntField(profile.Losses);
                fields["lastSeen"] = FirestoreClient.IntField(DateTimeOffset.UtcNow.ToUnixTimeSeconds());
                var paths = new[] { "progress", "level", "title", "wins", "losses", "lastSeen" };
                _db.PatchFields(PathOf(nickname), fields, paths,
                    null, error => Debug.LogWarning($"進行状況の保存に失敗しました: {error}"));

                onSettled?.Invoke(settlement);
            }, error => Debug.LogWarning($"進行状況の保存に失敗しました(プロフィール読み込み): {error}"));
        }

        public void Equip(string nickname, RewardInfo reward, Action onSaved, Action<string> onError)
        {
            WithCurrent(nickname, profile =>
            {
                if (!profile.Progress.IsUnlocked(reward)) { onError?.Invoke("まだロックされています。"); return; }
                var previous = profile.Progress.EquippedInSameSlot(reward);
                profile.Progress.Equip(reward);
                _db.PatchFields(PathOf(nickname), ProgressFields(profile), ProgressFieldPaths, onSaved, error =>
                {
                    if (previous != null) profile.Progress.Equip(previous);   // undo locally
                    onError?.Invoke(error);
                });
            }, onError);
        }

        // Moves the whole document to the new name: copy every field to players/{newName}, then
        // delete players/{oldName}, so the old nickname is freed instead of staying taken forever.
        public void Rename(string oldName, string newName, Action onRenamed, Action<string> onError)
        {
            string problem = NicknameProblem(newName);
            if (problem != null) { onError?.Invoke(problem); return; }

            _db.GetDocument(PathOf(oldName), oldDoc =>
            {
                var fields = oldDoc?["fields"] as JObject ?? new JObject();
                fields["nickname"] = FirestoreClient.StringField(newName);
                fields["lastSeen"] = FirestoreClient.IntField(DateTimeOffset.UtcNow.ToUnixTimeSeconds());

                // Create-only: if someone else took the name a moment ago, this fails cleanly.
                _db.CreateDocument(Collection, newName, fields, () =>
                {
                    if (Current != null && Current.Nickname == oldName) Current.Nickname = newName;
                    // The new document is the one that matters; a failed delete only leaves the
                    // old name reserved, so it is logged rather than reported as a failed rename.
                    _db.DeleteDocument(PathOf(oldName), null,
                        error => Debug.LogWarning($"以前のニックネームドキュメントの削除に失敗しました: {error}"));
                    onRenamed?.Invoke();
                },
                () => onError?.Invoke("すでに使用されているニックネームです。"),
                error => onError?.Invoke($"変更に失敗しました: {error}"));
            }, error => onError?.Invoke($"サーバーへの接続に失敗しました: {error}"));
        }
    }
}
