// GoogleTrackingID.cs
// 
// This file provides easy access to strings intended to be changed.
// 
// Change these strings to match the values given by your Google Analytics
// page after setting up an account and registering a new Web Page on 
// their site. (http://google.com/analytics)

namespace Assets.GAnalytics
{
    public partial class
    GAnalyticsObject
    {
        // NOTE: Must be the UID of a Web Page, not a Mobile App!
        //       Format is UA-XXXXXXXX-X
        private const string GoogleTrackingId = "UA-40810830-1";

        // Must be the domain set on your Google Analytics page
        private const string TrackingDomain = "www.deadweightgames.com";

        // Set this to any product name you wish to track
        private const string ProductName = "TestProduct";

        // Option flag for logging failed (offline) events to file to send later
        //
        // NOTE: Logged events that are later uploaded will have be timestamped
        //       from the upload time, not the original event time!
        private readonly static bool AreEventsLoggedOffline = false;
    }
}