namespace LiarsBatting.Network
{
    // Firebase Web API keys are meant to be shipped inside client code (that's
    // how every Firebase web/app project works) -- access control lives in
    // Firestore Security Rules, not in hiding this key. Still, tighten the
    // rules before this project goes further than a classroom demo.
    public static class FirebaseConfig
    {
        public const string ProjectId = "liarsbatting";
        public const string ApiKey = "AIzaSyD1ovqjcx61Rr6mYDYcW86CLNw0HBqdnoE";

        public const string BaseUrl =
            "https://firestore.googleapis.com/v1/projects/" + ProjectId + "/databases/(default)/documents";
    }
}
