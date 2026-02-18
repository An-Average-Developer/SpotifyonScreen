namespace SpotifyOnScreen.Configuration
{
    public static class AppVersion
    {
        public const string CURRENT_VERSION = "1.0.0";

        public const string GITHUB_OWNER = "An-Average-Developer";
        public const string GITHUB_REPO = "SpotifyonScreen";
        public const string GITHUB_BRANCH = "main";

        public static string GetDisplayVersion() => $"v{CURRENT_VERSION}";
        public static string GetGitHubRepoUrl() => $"https://github.com/{GITHUB_OWNER}/{GITHUB_REPO}";
    }
}
