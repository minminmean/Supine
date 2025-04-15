using System;

namespace Supine
{
    namespace Utilities
    {
        [Serializable]
        public struct GuidDictionary
        {
            public Animations animations; 
            public Controllers controllers;
            public Prefabs prefabs;
            public AppVersions app_versions;

            [Serializable]
            public struct Animations
            {
                public Sitting sitting;
                public Sleeping sleeping;
                public EX ex;

                [Serializable]
                public struct Sitting
                {
                    public string petan;
                    public string tatehiza_girl;
                    public string agura;
                    public string tatehiza_boy;
                }
                
                [Serializable]
                public struct Sleeping
                {
                    public string supine;
                    public string supine_r;
                    public string supine_l;
                    public string side_sleep;
                    public string side_sleep_c;
                    public string side_sleep_rev;
                }

                [Serializable]
                public struct EX
                {
                    public string mji;
                    public string disk;
                    public string kji;
                    public string kji_znk;
                    public string snk;
                    public string srag;
                }
            }

            [Serializable]
            public struct Controllers
            {
                public string normal;
                public string ex;                
            }

            [Serializable]
            public struct Prefabs
            {
                public string normal;
                public string ex;
            }

            [Serializable]
            public struct AppVersions
            {
                public string normal;
                public string ex;
            }
        }
    }
}