// GAnalyticsObject.cs
// A namespace for all Google Analytics-related functionality.
//
// Supports registration of Analytics page views called from code within this
// assembly, accessed on a common singleton gameobject which is fully managed
// and self-contained. All relevant object methods are accessible by a
// namespace helper class.

using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using UnityEngine;
using Random = UnityEngine.Random;

namespace Assets.GAnalytics
{
    /// <summary>
    ///     Helper class intended to use neat-looking syntax while calling
    ///     instantiated GAnalyticsObject methods.
    ///     example:
    ///     using Assets.GAnalytics;
    ///     ...
    ///     Analytics.RegisterView("ViewName");
    /// </summary>
    public static class
        Analytics
    {
        /// <summary>
        ///     Registers a View event with Google Analytics with a given page name.
        /// </summary>
        /// <param name="pageTitle">Name of web page visited</param>
        public static void
            RegisterView(string pageTitle)
        {
            GAnalyticsObject.Instance.RegisterView(pageTitle);
        }

        /// <summary>
        ///     Registers any page event with category, action, and optional values.
        /// </summary>
        /// <param name="pageTitle">Name of web page being visited.</param>
        /// <param name="category">Category of this event.</param>
        /// <param name="action">Action being performed under this event category.</param>
        /// <param name="label">(Optional) Label assigned to this event instance.</param>
        /// <param name="value">(Optional) Integer value assigned to this event label.</param>
        public static void
            RegisterEvent(string pageTitle, string category, string action, string label = "", int value = 0)
        {
            GAnalyticsObject.Instance.RegisterEvent(pageTitle, category, action, label, value);
        }

        /// <summary>
        ///     Remove all Analytics event entries logged locally.
        /// </summary>
        public static void
            EraseLogs()
        {
            GAnalyticsObject.Instance.PurgeLoggedEvents();
        }
    }

    /// <summary>
    ///     A singleton Unity gameObject (required to run Co-routines with WWW
    ///     calls) encapsulating all behaviour required to communicate with a
    ///     set-up Google Analytics account.
    ///     Note that the class is partial in order to fragment the user-required
    ///     fields into a more easily-accessible file. (See GoogleTrackingID.cs)
    /// </summary>
    public partial class
        GAnalyticsObject : MonoBehaviour
    {
        // singleton - object will be created if referenced before instantiated
        private static GAnalyticsObject _instance;

        public static GAnalyticsObject Instance
        {
            get
            {
                if (_instance == null)
                {
                    GameObject gaGameObject = new GameObject("_Analytics");
                    _instance = gaGameObject.AddComponent<GAnalyticsObject>();
                    DontDestroyOnLoad(gaGameObject);
                }
                return _instance;
            }
        }

        //
        // Class Members
        // 

        // GIF request parameters - full document support at
        // https://developers.google.com/analytics/resources/articles/gaTrackingTroubleshooting#gifParameters
        private static readonly Dictionary<string, string> UrlFields = new Dictionary<string, string>();

        // constant strings used to access PlayerPrefs members
        // for fields
        private const string PrefsPrefix = "GAnalytics";
        private const string PrefsCookieId = PrefsPrefix + "CookieID";
        private const string PrefsFirstRun = PrefsPrefix + "FirstRun";
        private const string PrefsLastRun = PrefsPrefix + "LastRun";
        private const string PrefsVisits = PrefsPrefix + "Visits";

        // for logged events
        private const string PrefsLogCount = PrefsPrefix + "LogCount";
        private const string PrefsLogPrefix = PrefsPrefix + "Log";

        // Number of frames to wait after successfully posting a logged event
        // before uploading the next
        private const uint NumFramesWaitBetweenLogPosts = 1U;

#if UNITY_WEBPLAYER       
    // Web Player builds limit the PlayerPrefs filesize at 1024 kb
    // so it makes sense to impose a limit
            private const uint NUM_MAX_LOG_POSTS = 256U;
#else
        private const uint NumMaxLogPosts = int.MaxValue;
#endif

        // analytics session variables
        private int _cookieId;
        private int _siteId;

        private int _lastUpdate;
        private string _lastPage;
        private int _totalVisits;

        private int _sessionStart;
        private int _lastRun;
        private int _firstRun;

        // other session variables
        private bool _hasUrlPostSuccess; // last URL post attempt was successful

        private bool _isPostingLogsMutex; // flag to stop simultaneous reposting of logs
        private bool _hasLoggedEvents; // cached to avoid reading from file constantly

        //
        // MonoBehavior Methods
        //

        /// <summary>
        ///     Check if this is the only active GAnalyticsObject component in scene.
        /// </summary>
        private void
            Awake()
        {
            if (FindSceneObjectsOfType(typeof (GAnalyticsObject)).Length != 1)
            {
                Debug.LogWarning(PrefsPrefix + " Cannot add multiple instances of GAnalyticsObject component to scene.");
                DestroyImmediate(this);
                return;
            }

            // initialize before other methods can be called on this
            Initialize();
        }

        private void
            Update()
        {
            // post logged events, called in Update so that user can erase
            // logs before a session begins without the old logs being sent.
            if (AreEventsLoggedOffline && _hasLoggedEvents && _hasUrlPostSuccess)
            {
                StartCoroutine(PostLoggedEvents());
            }

            // TODO: If logging enabled, check for re-establishing connection
        }

        //
        // Class Methods
        //

        /// <summary>
        ///     Set up analytics session variables and register a pageview event to
        ///     begin the tracking session.
        /// </summary>
        private void
            Initialize()
        {
            int currentEpoch = GetEpoch();

            _siteId = TrackingDomain.GetHashCode();

            _cookieId = PlayerPrefs.GetInt(PrefsCookieId, -1);
            if (_cookieId == -1)
            {
                _cookieId = Random.Range(0, int.MaxValue);
                PlayerPrefs.SetInt(PrefsCookieId, _cookieId);

                _firstRun = currentEpoch;
                PlayerPrefs.SetInt(PrefsFirstRun, _firstRun);
            }
            else
            {
                _firstRun = PlayerPrefs.GetInt(PrefsFirstRun, currentEpoch);
            }

            _sessionStart = currentEpoch;

            _lastRun = PlayerPrefs.GetInt(PrefsLastRun, currentEpoch);
            PlayerPrefs.SetInt(PrefsLastRun, _sessionStart);

            _totalVisits = PlayerPrefs.GetInt(PrefsVisits, 0);
            PlayerPrefs.SetInt(PrefsVisits, ++_totalVisits);

            _lastPage = null;

            // set up non-changing fields
            // fields that don't change
            UrlFields["utmje"] = "0";
            UrlFields["utmcs"] = "-";
            UrlFields["utmfl"] = "-";
            UrlFields["utmcr"] = "1";
            UrlFields["utmwv"] = "4.6.5";
            UrlFields["utmac"] = GoogleTrackingId;
            UrlFields["utmul"] = "en";
            UrlFields["utmhn"] = WWW.EscapeURL(TrackingDomain);
            UrlFields["utmsr"] = string.Format("{0}x{1}", Screen.width, Screen.height);
            UrlFields["utmsc"] = WWW.EscapeURL("24-bit"); // TODO: consider changing the bit depth

            // send first
            RegisterView(String.Empty);

            // check logging history
            _hasLoggedEvents = PlayerPrefs.GetInt(PrefsLogCount, 0) != 0;
        }

        /// <summary>
        ///     Submit to Google Analytics any name of a (web) page visited.
        /// </summary>
        /// <param name="pageTitle">Name of web page being visited.</param>
        public void
            RegisterView(string pageTitle)
        {
            UpdateFields(pageTitle);

            // send the newly constructed URL to Google
            UrlGet(ConstructUrl());
        }

        /// <summary>
        ///     Submit to Google Analytics any page event with category, action, and optional values.
        /// </summary>
        /// <param name="pageTitle">Name of web page being visited.</param>
        /// <param name="category">Category of this event.</param>
        /// <param name="action">Action being performed under this event category.</param>
        /// <param name="label">(Optional) Label assigned to this event instance.</param>
        /// <param name="value">(Optional) Integer value assigned to this event label.</param>
        public void
            RegisterEvent(string pageTitle, string category, string action, string label = "", int value = 0)
        {
            UpdateFields(pageTitle);

            if (label.Equals(String.Empty))
            {
                AddEventFields(category, action);
            }
            else
            {
                AddEventFields(category, action, label, value);
            }

            // send the newly constructed URL to Google
            UrlGet(ConstructUrl());

            // clean up afterwards
            RemoveEventFields();
        }

        /// <summary>
        ///     Construct the URL required to register a page view event with
        ///     Google Analytics required for a URL GET routine.
        /// </summary>
        /// <param name="pageTitle">Name of web page being visited.</param>
        private void
            UpdateFields(string pageTitle)
        {
            // update fields that change between calls

            // NOTE: WWW.EscapeURL turns spaces into "+" characters, giving a 
            //       400 Bad Request error in page names with spaces.
            //       Replacing spaces with %20 seems to work instead.
            pageTitle = pageTitle.Replace(" ", "%20");
            pageTitle = WWW.EscapeURL(pageTitle);

            UrlFields["utmdt"] = pageTitle;

            if (_lastPage != null)
            {
                UrlFields["utmr"] = WWW.EscapeURL(string.Format("http://{0}/{1}", TrackingDomain, _lastPage));
            }
            UrlFields["utmp"] = _lastPage = WWW.EscapeURL(ProductName) + "/" + pageTitle;

            UrlFields["utmn"] = Random.Range(0, int.MaxValue).ToString(CultureInfo.InvariantCulture);

            string utmb = string.Format("__utmb={0};", _siteId);
            string utmc = string.Format("__utmc={0};", _siteId);
            string utma = string.Format("__utma={0}.{1}.{2}.{3}.{4}.{5};", _siteId, _cookieId, _firstRun, _lastRun,
                _sessionStart, _totalVisits);
            string utmz = string.Format("__utmz={0}.{1}.{2}.1.utmccn=(direct)|utmcsr=(direct)|utmcmd=(none);", _siteId,
                GetEpoch(), _totalVisits);

            UrlFields["utmcc"] = WWW.EscapeURL(string.Format("{0}+{1}+{2}+{3}", utma, utmb, utmc, utmz));
        }

        /// <summary>
        ///     Construct additional fields required to submit event information to
        ///     Google Analytics.
        /// </summary>
        /// <param name="category">Category of this event.</param>
        /// <param name="action">Action being performed under this event category.</param>
        private void
            AddEventFields(string category, string action)
        {
            string utme = string.Format("5({0}*{1})", category, action);

            UrlFields["utme"] = utme;
            UrlFields["utmt"] = "event";
        }

        /// <summary>
        ///     Construct additional fields required to submit event information to
        ///     Google Analytics.
        /// </summary>
        /// <param name="category">Category of this event.</param>
        /// <param name="action">Action being performed under this event category.</param>
        /// <param name="label">(Optional) Label assigned to this event instance.</param>
        /// <param name="value">(Optional) Integer value assigned to this event label.</param>
        private void
            AddEventFields(string category, string action, string label, int value)
        {
            string utme = string.Format("5({0}*{1}*{2})({3})", category, action, label, value);

            UrlFields["utme"] = utme;
            UrlFields["utmt"] = "event";
        }

        /// <summary>
        ///     Cleanup method to remove event-specific fields for following
        ///     non-event usage.
        /// </summary>
        private void
            RemoveEventFields()
        {
            UrlFields.Remove("utme");
            UrlFields.Remove("utmt");
        }

        /// <summary>
        ///     Constructs the full URL string from fields stored in a dictionary
        ///     then sends a HTTP GET request using said string.
        /// </summary>
        /// <returns>Fully formatted URL to submit an analytics event</returns>
        private string
            ConstructUrl()
        {
            // construct the url from previously assigned fields
            string url = UrlFields.Aggregate("http://www.google-analytics.com/__utm.gif?",
                (current, field) => current + string.Format("{0}={1}&", field.Key, field.Value));

            url = url.Substring(0, url.Length - 1);

            return url;
        }

        /// <summary>
        ///     An Epoch serves as an origin point of time used as a reference.
        /// </summary>
        /// <returns>Unique timestamp.</returns>
        private int
            GetEpoch()
        {
            return (int) (DateTime.UtcNow - new DateTime(1970, 1, 1)).TotalSeconds;
        }

        /// <summary>
        ///     Start a coroutine requesting a HTTP GET with the given URL.
        /// </summary>
        /// <param name="url">The URL to send a GET request to.</param>
        private void
            UrlGet(string url)
        {
            StartCoroutine(WaitForRequest(new WWW(url)));
        }

        /// <summary>
        ///     Coroutine used to actually run a WWW object request and log an
        ///     error if failed.
        /// </summary>
        /// <param name="www">WWW object of URL and optional fields.</param>
        /// <returns>IEnumerator</returns>
        private IEnumerator
            WaitForRequest(WWW www)
        {
            yield return www;

            _hasUrlPostSuccess = www.error == null;

            if (!_hasUrlPostSuccess)
            {
                Debug.LogError(PrefsPrefix + " WWW Error: " + www.error + "\nURL is: [" + www.url + "]");

                if (AreEventsLoggedOffline)
                {
                    // transmission to Google Analytics failed, write it to file
                    // for later submission (session when connection restored)
                    LogEventOffline(www.url);
                }
            }
        }

        /// <summary>
        ///     Coroutine to read each event URL string from file and attempts to
        ///     post said URL to Google Analytics.
        ///     TODO: Interruption handling.
        /// </summary>
        /// <returns>IEnumerator</returns>
        private IEnumerator
            PostLoggedEvents()
        {
            // if this coroutine isn't already running..
            if (!_isPostingLogsMutex)
            {
                _isPostingLogsMutex = true;

                int logCount = PlayerPrefs.GetInt(PrefsLogCount, 0);

                // Work backwards through the logs in a Stack fashion so that if 
                // the connection terminates again, we can resume simply from end.
                for (int i = logCount - 1; i != -1; --i)
                {
                    string url = PlayerPrefs.GetString(PrefsLogPrefix + i);

                    WWW www = new WWW(url);

                    yield return www;

                    _hasUrlPostSuccess = www.error == null;

                    if (!_hasUrlPostSuccess)
                    {
                        // abort the coroutine, print the URL in case of malformed URL errors
                        Debug.LogWarning(PrefsPrefix + "Logged event restoration aborted due to WWW Error: " +
                                         www.error + "\nURL is: [" + www.url + "]");
                        i = 0;
                    }
                    else
                    {
                        // erase the posted event log from playerprefs
                        PlayerPrefs.DeleteKey(PrefsLogPrefix + i);
                        logCount--;

                        // wait the requested number of frames before removing next
                        for (int f = 0; f != NumFramesWaitBetweenLogPosts; ++f)
                        {
                            yield return null;
                        }
                    }

                    // for last entry
                    if (logCount == 0)
                    {
                        _hasLoggedEvents = false;
                    }
                }

                // save the updated index
                PlayerPrefs.SetInt(PrefsLogCount, logCount);

                // free up this coroutine to be ran again
                _isPostingLogsMutex = false;
            }

            yield return null;
        }

        /// <summary>
        ///     Starts a coroutine on the instanced gameObject to begin removing
        ///     individual logs from the PlayerPrefs save registry
        /// </summary>
        public void
            PurgeLoggedEvents()
        {
            // delete the existing logged events as a coroutine
            StartCoroutine(PurgeLoggedEventsRoutine());
        }

        /// <summary>
        ///     Coroutine removing individual log entries from the save registry
        /// </summary>
        /// <returns>IEnumerator</returns>
        private IEnumerator
            PurgeLoggedEventsRoutine()
        {
            int logCount = PlayerPrefs.GetInt(PrefsLogCount, 0);

            // remove entry from file, for the number of logs counted in file
            for (int i = 0; i != logCount; ++i)
            {
                // if (PlayerPrefs.HasKey(PREFS_LOGPREFIX + i.ToString()))
                PlayerPrefs.DeleteKey(PrefsLogPrefix + i);

                yield return null;
            }

            // reset the index
            PlayerPrefs.SetInt(PrefsLogCount, 0);

            yield return null;
        }

        /// <summary>
        ///     Writes a URL string to file via PlayerPrefs when said URL cannot be
        ///     accessed (eg: no internet access) for posting later.
        /// </summary>
        /// <param name="urlString">The full analytics event URL to be logged</param>
        private void
            LogEventOffline(string urlString)
        {
            int nextLogIndex = PlayerPrefs.GetInt(PrefsLogCount, 0);

            if (nextLogIndex != NumMaxLogPosts)
            {
                // write url string to file under this index, then increment the index
                PlayerPrefs.SetString(PrefsLogPrefix + nextLogIndex, urlString);
                PlayerPrefs.SetInt(PrefsLogCount, ++nextLogIndex);

                _hasLoggedEvents = true;
            }
            else
            {
                // logs full, throw warning
                Debug.LogWarning(PrefsPrefix + "Event Logging full! (" + nextLogIndex + " entries!)");
            }
        }
    }
}