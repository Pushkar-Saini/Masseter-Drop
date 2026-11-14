namespace MasseterDrop.Services
{
    /// <summary>
    /// Centralized configuration for PostHog product analytics.
    /// Configure your public PostHog project token and regional ingestion host here for release builds.
    /// </summary>
    public static class AnalyticsConfig
    {
        /// <summary>
        /// PostHog Project API Key / Token.
        /// PostHog project tokens (phc_...) have ingestion-only permissions and are safe for client-side distribution.
        /// </summary>
        public const string ProjectToken = "phc_oMYyz3tQWYX3oTpWu2FmKs6ZjncDGrZJFpRJG94P2f7o";

        /// <summary>
        /// Regional PostHog Ingestion Host URL.
        /// - US Cloud: "https://us.i.posthog.com" (default)
        /// - EU Cloud: "https://eu.i.posthog.com"
        /// </summary>
        public const string IngestionHost = "https://us.i.posthog.com";
    }
}
