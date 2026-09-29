using System;
using System.Collections;
using System.Text;
using Newtonsoft.Json.Linq;
using UnityEngine;
using UnityEngine.Networking;

namespace LiarsBatting.Network
{
    // Minimal Firestore REST client. Deliberately not the native Firebase SDK --
    // that one has real gaps on Windows/WebGL standalone builds. UnityWebRequest
    // works identically on every platform Unity targets, at the cost of writing
    // our own thin layer over Firestore's typed JSON document format.
    public class FirestoreClient
    {
        private readonly MonoBehaviour _host;

        public FirestoreClient(MonoBehaviour host)
        {
            _host = host;
        }

        public void GetDocument(string path, Action<JObject> onSuccess, Action<string> onError)
            => _host.StartCoroutine(GetDocumentCo(path, onSuccess, onError));

        private IEnumerator GetDocumentCo(string path, Action<JObject> onSuccess, Action<string> onError)
        {
            string url = $"{FirebaseConfig.BaseUrl}/{path}?key={FirebaseConfig.ApiKey}";
            using var req = UnityWebRequest.Get(url);
            yield return req.SendWebRequest();

            if (req.result == UnityWebRequest.Result.Success)
                onSuccess?.Invoke(JObject.Parse(req.downloadHandler.text));
            else if (req.responseCode == 404)
                onSuccess?.Invoke(null); // no document at this path yet
            else
                onError?.Invoke(DescribeError(req));
        }

        // Create-or-replace. Firestore's PATCH on a specific document path does
        // both, which is exactly the "set" semantics this game needs.
        public void SetDocument(string path, JObject fields, Action onSuccess, Action<string> onError)
            => _host.StartCoroutine(SetDocumentCo(path, fields, onSuccess, onError));

        private IEnumerator SetDocumentCo(string path, JObject fields, Action onSuccess, Action<string> onError)
        {
            string url = $"{FirebaseConfig.BaseUrl}/{path}?key={FirebaseConfig.ApiKey}";
            var body = new JObject { ["fields"] = fields };
            byte[] bodyBytes = Encoding.UTF8.GetBytes(body.ToString());

            using var req = new UnityWebRequest(url, "PATCH");
            req.uploadHandler = new UploadHandlerRaw(bodyBytes);
            req.downloadHandler = new DownloadHandlerBuffer();
            req.SetRequestHeader("Content-Type", "application/json");
            yield return req.SendWebRequest();

            if (req.result == UnityWebRequest.Result.Success) onSuccess?.Invoke();
            else onError?.Invoke(DescribeError(req));
        }

        // Updates ONLY the named fields, leaving everything else in the document
        // untouched (and creates the document if it doesn't exist yet). Unlike
        // SetDocument's full replace, two clients can safely patch DIFFERENT
        // fields on the same document at the same time without either one
        // clobbering what the other just wrote.
        public void PatchFields(string path, JObject fields, string[] fieldPaths,
            Action onSuccess, Action<string> onError)
            => _host.StartCoroutine(PatchFieldsCo(path, fields, fieldPaths, onSuccess, onError));

        private IEnumerator PatchFieldsCo(string path, JObject fields, string[] fieldPaths,
            Action onSuccess, Action<string> onError)
        {
            var mask = new StringBuilder();
            foreach (var f in fieldPaths)
                mask.Append("&updateMask.fieldPaths=").Append(UnityWebRequest.EscapeURL(f));
            string url = $"{FirebaseConfig.BaseUrl}/{path}?key={FirebaseConfig.ApiKey}{mask}";

            var body = new JObject { ["fields"] = fields };
            byte[] bodyBytes = Encoding.UTF8.GetBytes(body.ToString());

            using var req = new UnityWebRequest(url, "PATCH");
            req.uploadHandler = new UploadHandlerRaw(bodyBytes);
            req.downloadHandler = new DownloadHandlerBuffer();
            req.SetRequestHeader("Content-Type", "application/json");
            yield return req.SendWebRequest();

            if (req.result == UnityWebRequest.Result.Success) onSuccess?.Invoke();
            else onError?.Invoke(DescribeError(req));
        }

        public void ListCollection(string path, Action<JArray> onSuccess, Action<string> onError)
            => _host.StartCoroutine(ListCollectionCo(path, onSuccess, onError));

        private IEnumerator ListCollectionCo(string path, Action<JArray> onSuccess, Action<string> onError)
        {
            string url = $"{FirebaseConfig.BaseUrl}/{path}?key={FirebaseConfig.ApiKey}";
            using var req = UnityWebRequest.Get(url);
            yield return req.SendWebRequest();

            if (req.result == UnityWebRequest.Result.Success)
            {
                var obj = JObject.Parse(req.downloadHandler.text);
                onSuccess?.Invoke(obj["documents"] as JArray ?? new JArray());
            }
            else
            {
                onError?.Invoke(DescribeError(req));
            }
        }

        public void DeleteDocument(string path, Action onSuccess, Action<string> onError)
            => _host.StartCoroutine(DeleteDocumentCo(path, onSuccess, onError));

        private IEnumerator DeleteDocumentCo(string path, Action onSuccess, Action<string> onError)
        {
            string url = $"{FirebaseConfig.BaseUrl}/{path}?key={FirebaseConfig.ApiKey}";
            using var req = UnityWebRequest.Delete(url);
            yield return req.SendWebRequest();

            if (req.result == UnityWebRequest.Result.Success) onSuccess?.Invoke();
            else onError?.Invoke(DescribeError(req));
        }

        private static string DescribeError(UnityWebRequest req)
        {
            string body = req.downloadHandler != null ? req.downloadHandler.text : "";
            return $"({req.responseCode}) {req.error} {body}";
        }

        // ---- Firestore's typed field format: {"fields": {"name": {"stringValue": "..."}}} ----

        public static JObject StringField(string value) => new JObject { ["stringValue"] = value };
        public static JObject IntField(long value) => new JObject { ["integerValue"] = value.ToString() };
        public static JObject BoolField(bool value) => new JObject { ["booleanValue"] = value };

        public static string ReadString(JObject doc, string field, string fallback = null)
            => doc?["fields"]?[field]?["stringValue"]?.ToString() ?? fallback;

        public static long ReadInt(JObject doc, string field, long fallback = 0)
        {
            var raw = doc?["fields"]?[field]?["integerValue"]?.ToString();
            return raw != null && long.TryParse(raw, out var value) ? value : fallback;
        }

        public static bool ReadBool(JObject doc, string field, bool fallback = false)
            => doc?["fields"]?[field]?["booleanValue"]?.Value<bool?>() ?? fallback;

        // Some "turn_done" writes carry an extra field (e.g. priestAnswerBool)
        // and others don't -- ReadBool/ReadInt's fallback alone can't tell "field
        // absent" apart from "field present with the fallback's own value".
        public static bool HasField(JObject doc, string field) => doc?["fields"]?[field] != null;

        // Firestore document names come back as full resource paths
        // ("projects/x/databases/(default)/documents/players/abc"); this is the
        // last path segment, i.e. the document ID.
        public static string ReadDocumentId(JObject doc)
        {
            string name = doc?["name"]?.ToString();
            if (string.IsNullOrEmpty(name)) return null;
            int lastSlash = name.LastIndexOf('/');
            return lastSlash >= 0 ? name.Substring(lastSlash + 1) : name;
        }
    }
}
