   [Unity3D Google Analytics]

   Intended for open modification and redistribution via GitHub accessible
   at https://github.com/burningship/unity3d-ganalytics.

   Enhanced Google Analytics support for the web & desktop standalone 
   platforms inside Unity3d. Supports optional logging of registered 
   Analytics events in the case of an unsuccessful network connection 
   attempt. Events are written to file via PlayerPrefs and transmitted if a
   working connection is restored, including between sessions.
   
   
   [Usage]
   
   Set up your Google Analytics account online (analytics.google.com) for Web
   Page tracking. Noting the domain and UID values assigned to this account.
   
   In Unity3D, assign these values to the relevant strings inside the
   GoogleTrackingID.cs script file. Assign a product name and enable logging 
   if you wish.
   
   Intended use is to import the Assets.GAnalytics namespace and call
   Analytics.Registerview() methods to send events from any script in the 
   Unity3D project.


   Copyright 2013 [Liam David Jenkin]

   Licensed under the Apache License, Version 2.0 (the "License");
   you may not use this file except in compliance with the License.
   You may obtain a copy of the License at

       http://www.apache.org/licenses/LICENSE-2.0

   Unless required by applicable law or agreed to in writing, software
   distributed under the License is distributed on an "AS IS" BASIS,
   WITHOUT WARRANTIES OR CONDITIONS OF ANY KIND, either express or implied.
   See the License for the specific language governing permissions and
   limitations under the License.

   Licensed under [Apache License, Version 2.0]
