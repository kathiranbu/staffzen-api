using FirebaseAdmin;
using FirebaseAdmin.Messaging;
using Google.Apis.Auth.OAuth2;

namespace APM.StaffZen.API.Services
{
    /// <summary>
    /// Sends Firebase Cloud Messaging push notifications using FCM HTTP V1 API
    /// via the official FirebaseAdmin SDK and Service Account credentials.
    /// The service account JSON file is read from the path configured in
    /// appsettings.json: Firebase:ServiceAccountPath
    /// </summary>
    public class FirebaseService
    {
        private readonly ILogger<FirebaseService> _logger;
        private readonly IConfiguration _config;
        private bool _initialized = false;
        private readonly object _lock = new();

        public FirebaseService(ILogger<FirebaseService> logger, IConfiguration config)
        {
            _logger = logger;
            _config = config;
        }

        /// <summary>
        /// Initializes the FirebaseApp singleton. Safe to call multiple times.
        /// </summary>
        private void EnsureInitialized()
        {
            if (_initialized) return;
            lock (_lock)
            {
                if (_initialized) return;

                // Already initialized by another service instance?
                if (FirebaseApp.DefaultInstance != null)
                {
                    _initialized = true;
                    return;
                }

                var serviceAccountPath = _config["Firebase:ServiceAccountPath"];
                if (string.IsNullOrWhiteSpace(serviceAccountPath))
                {
                    _logger.LogError("Firebase:ServiceAccountPath is not configured in appsettings.json");
                    return;
                }

                // Resolve relative path from the app's base directory
                if (!Path.IsPathRooted(serviceAccountPath))
                    serviceAccountPath = Path.Combine(AppContext.BaseDirectory, serviceAccountPath);

                if (!File.Exists(serviceAccountPath))
                {
                    _logger.LogError("Firebase service account file not found: {Path}", serviceAccountPath);
                    return;
                }

                FirebaseApp.Create(new AppOptions
                {
                    Credential = GoogleCredential.FromFile(serviceAccountPath)
                });

                _initialized = true;
                _logger.LogInformation("FirebaseAdmin SDK initialized from {Path}", serviceAccountPath);
            }
        }

        /// <summary>
        /// Sends a push notification to a single device via its FCM registration token.
        /// </summary>
        public async Task<bool> SendPushAsync(string fcmToken, string title, string body)
        {
            if (string.IsNullOrWhiteSpace(fcmToken))
            {
                _logger.LogWarning("FCM token is empty — push skipped.");
                return false;
            }

            try
            {
                EnsureInitialized();

                if (FirebaseApp.DefaultInstance == null)
                {
                    _logger.LogError("FirebaseApp not initialized — push skipped.");
                    return false;
                }

                var message = new Message
                {
                    Token = fcmToken,
                    Notification = new Notification
                    {
                        Title = title,
                        Body  = body,
                    },
                    Webpush = new WebpushConfig
                    {
                        Notification = new WebpushNotification
                        {
                            Title = title,
                            Body  = body,
                            Icon  = "/favicon.png",
                        },
                        FcmOptions = new WebpushFcmOptions
                        {
                            Link = "/"
                        }
                    },
                    Data = new Dictionary<string, string>
                    {
                        { "title", title },
                        { "body",  body  }
                    }
                };

                var response = await FirebaseMessaging.DefaultInstance.SendAsync(message);
                _logger.LogInformation("FCM push sent. MessageId: {Id}", response);
                return true;
            }
            catch (FirebaseMessagingException fex) when (
                fex.MessagingErrorCode == MessagingErrorCode.Unregistered ||
                fex.MessagingErrorCode == MessagingErrorCode.InvalidArgument)
            {
                // Token is stale/invalid — log but don't crash
                _logger.LogWarning("FCM token invalid or unregistered for token starting {Token}: {Msg}",
                    fcmToken.Length > 10 ? fcmToken[..10] : fcmToken, fex.Message);
                return false;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "FirebaseService.SendPushAsync failed");
                return false;
            }
        }
    }
}
