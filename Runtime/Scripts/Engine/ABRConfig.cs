/* ABRConfig.cs
 *
 * Copyright (c) 2021 University of Minnesota
 * Authors: Bridger Herman <herma582@umn.edu>
 *
 * This program is free software: you can redistribute it and/or modify
 * it under the terms of the GNU General Public License as published by
 * the Free Software Foundation, either version 3 of the License, or
 * (at your option) any later version.
 *
 * This program is distributed in the hope that it will be useful,
 * but WITHOUT ANY WARRANTY; without even the implied warranty of
 * MERCHANTABILITY or FITNESS FOR A PARTICULAR PURPOSE.  See the
 * GNU General Public License for more details.
 *
 * You should have received a copy of the GNU General Public License
 * along with this program.  If not, see <https://www.gnu.org/licenses/>.
 */

using System;
using System.Net.Http;
using System.Linq;
using System.IO;
using System.Collections.Generic;
using System.Reflection;
using UnityEngine;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using Newtonsoft.Json.Schema;
using System.Diagnostics.Tracing;

namespace IVLab.ABREngine
{
    /// <summary>
    /// This Scriptable Object controls the ABR configuration before startup. On
    /// engine startup, a copy is instantiated for use at runtime.
    /// </summary>
    /// 
    /// [CreateAssetMenu(fileName = "ABRConfig", menuName = "ABR/ABR Configuration")]
    public class ABRConfig : ScriptableObject
    { 
        /// Read config stuff in from a JSON file <summary>
             
        public string abr_root{get; set;} = null;
        
        [System.Serializable]
        public class jvector
        {
            public float x;
            public float y;
            public float z;
        };

        [System.Serializable]
        public class RemoteDataSource
        {
            public string host;
            public int port;
        };

        public class ExternalDataContainer
        {
            public jvector center;
            public jvector extent;
        }

        [System.Serializable]
        public class ExternalConfiguration
        {
            public string mediaDirectory;
            public string serverURL;     
            public bool useAutoDataContainer;   
            public jvector center;
            public jvector rotation;
            public double scale;
            public ExternalDataContainer container;
            public RemoteDataSource[] remotes;
        };

        [Header("Common Configuration Options (hover for more info)")]

        /// <summary>
        ///     Local path to look for datasets and visassets at. Default:
        ///     Application.persistentDataPath
        /// </summary>
        [Tooltip("Location on this computer where VisAssets and Datasets are located. Relative paths (e.g. `../media`) are also acceptable.")]
        public string mediaPath;

        /// <summary>
        ///     What server to connect to, if any. If provided, ABR will try to
        ///     register with the server immediately upon startup. Default: null
        /// </summary>
        [Tooltip("Full URL of the ABR server / visualization manager that this app should connect to. Leave blank for no server.")]
        public string serverUrl;

        /// <summary>
        /// Load a state from resources on ABREngine startup
        /// </summary>
        [Tooltip("Load a state from Resources or StreamingAssets folder on ABREngine startup. Example: `testState.json` Leave blank for no startup state.")]
        public string loadStateOnStart;

        [Header("Styling Defaults")]

        /// <summary>
        ///     The default prefab to look for in any resources folder
        /// </summary>
        [Tooltip("Default shape/color for glyphs in the Glyph layer")]
        public GameObject defaultGlyph;

        [Tooltip("Default color for geometries that have not had a colormap applied yet")]
        public Color defaultColor;

        [Tooltip("Default color for NaN values")]
        public Color defaultNanColor;

        [Tooltip("Default texture for NaN values on surfaces")]
        public Texture2D defaultNanTexture;

        [Tooltip("Default line texture for NaN values on ribbons")]
        public Texture2D defaultNanLine;

        [Header("Network-Based VisAssets and Data Configuration")]

        /// <summary>
        ///     What server to obtain VisAssets from, if any. If none provided,
        ///     ABR will assume that everything is in Unity's persistentData
        ///     path. If server is provided and resource doesn't exist in
        ///     persistentData, it will be downloaded. Default: null
        /// </summary>
        [Tooltip("Server to load VisAssets from (e.g. `http://192.168.137.1:8000/media/visassets`")]
        public string visAssetServerUrl;

        /// <summary>
        ///     What server to obtain data from, if any. If none provided,
        ///     ABR will assume that everything is in Unity's persistentData
        ///     path. If server is provided and resource doesn't exist in
        ///     persistentData, it will be downloaded. Default: null
        /// </summary>
        [Tooltip("Server to load VisAssets from (e.g. `http://192.168.137.1:8001/api`")]
        public string dataServerUrl;

        /// <summary>
        ///     Port to listen for data on, if any. Useful if, for instance, you want to
        ///     have a live connection to ParaView that pushes data into ABR. Default:
        ///     null
        /// </summary>
        [Tooltip("Port to listen for data connections (e.g. from ParaView on). A port `0` is assumed to mean no connection.")]
        public int dataListenerPort = 1900;

        [Header("Data Container Options")]
        /// <summary>
        /// Controls whether or not the <see cref="dataContainer"/> is used.
        /// </summary>
        [Tooltip("Use the automatic data container, or just import coordinates as-is")]
        public bool useAutoDataContainer;

        public RemoteDataSource[] remotes;

        public double scale;
        public Vector3 center;
        public Vector3 rotation;

        /// <summary>
        ///     Default bounds for datasets when showing (in Unity world coordinates)
        /// </summary>
        [Tooltip("Unity world-space container to automatically 'squish' all data into to avoid overflowing Unity coordinates")]
        public Bounds dataContainer = new Bounds(new Vector3(0, 0, 0), new Vector3(2, 2, 2));

        /// <summary>
        /// Override transform matrices for specific data impression groups.
        /// This is useful to change the data-to-Unity mapping if you need more
        /// fine-grained control than the auto-data-container gives you.
        /// </summary>
        [Tooltip("Unity world-space container to 'squish' all data into to avoid overflowing Unity coordinates")]
        public List<GroupToDataMatrixOverrideFields> overrideGroupToDataMatrices;


        /// <summary>
        ///     The Json Schema to use for validation of ABR states
        /// </summary>
        public JSchema Schema { get; private set; } 
 
        /// <summary>
        ///     Schema to use for internally grabbing default values
        /// </summary>
        public JObject SchemaJson { get; private set; }

        /// <summary>
        ///     Actual URI-ified URL of the server that ABR should connect to
        /// </summary>
        public Uri ServerUrl { get => new Uri(serverUrl); }

        /// <summary>
        /// Where to find the Schema online
        /// </summary>
        private const string SchemaUrl = "https://raw.githubusercontent.com/ivlab/abr-schema/master/";
        private string schemaName = "ABRSchema_0-2-0.json";

        void Reset()
        {
            mediaPath = Application.persistentDataPath;
            serverUrl = "";
            loadStateOnStart = "";
            dataContainer = new Bounds(Vector3.zero, Vector3.one * 2.0f);

            defaultGlyph = Resources.Load<GameObject>("DefaultSphere");
            defaultGlyph.SetActive(false);
            defaultColor = Color.white;
            defaultNanColor = Color.yellow;
            defaultNanTexture = null;
            defaultNanLine = null;

            visAssetServerUrl = "";
            dataServerUrl = "";
            dataListenerPort = 0;

            overrideGroupToDataMatrices = new List<GroupToDataMatrixOverrideFields>();
        }

        void Awake()
        {
            // Debug.Log("ABR Config Loaded");
            defaultGlyph = Resources.Load<GameObject>("DefaultSphere");
            defaultGlyph.SetActive(false);
            
            // Check for a backed up schema
            string backupSchemaDir = Path.Combine(Application.streamingAssetsPath, "schemas");
            string backupSchema = null;
            try
            {
                List<string> schemas = Directory.GetFiles(backupSchemaDir).Where(f => f.EndsWith(".json")).ToList();
                schemas.Sort();
                schemas.Reverse();
                backupSchema = schemas[0];
            }
            catch
            {
                Debug.LogErrorFormat("Unable to find a backup schema in {0}", backupSchemaDir);
            }

            if (backupSchema != null)
            {
                backupSchema = Path.Combine(backupSchemaDir, backupSchema);
            }

            // Load the schema
            HttpResponseMessage resp = ABREngine.httpClient.GetAsync(SchemaUrl + schemaName).Result;
            string schemaContents = null;
            if (!resp.IsSuccessStatusCode)
            {
                Debug.LogErrorFormat("Unable to load schema from {0}, using backup schema {1}", SchemaUrl + schemaName, backupSchema);
                using (StreamReader reader = new StreamReader(backupSchema))
                {
                    schemaContents = reader.ReadToEnd();
                }
            }
            else
            {
                schemaContents = (resp.Content.ReadAsStringAsync().Result);
            }

            Schema = JSchema.Parse(schemaContents);
            if (Schema == null)
            {
                Debug.LogErrorFormat("Unable to parse schema `{0}`.", schemaName);
                return;
            }
            if (Schema.Valid ?? false)
            {
                Debug.LogErrorFormat("Schema `{0}` is invalid.", schemaName);
                return;
            }
            SchemaJson = JObject.Parse(schemaContents);

            // Save a copy if needed
            bool needBackup = true;
            if (backupSchema != null) {
                using (StreamReader reader = new StreamReader(backupSchema))
                {
                    string bakContents = reader.ReadToEnd();
                    if (bakContents == schemaContents)
                    {
                        needBackup = false;
                    }
                }
            }
            if (needBackup)
            {
                string schemaName = DateTime.Now.ToString("s", System.Globalization.CultureInfo.InvariantCulture).Replace(":", "_") + ".json";
                if (!Directory.Exists(backupSchemaDir))
                {
                    Directory.CreateDirectory(backupSchemaDir);
                }
                string schemaBakPath = Path.Combine(backupSchemaDir, schemaName);
                using (StreamWriter writer = new StreamWriter(schemaBakPath))
                {
                    writer.Write(schemaContents);
                }
                Debug.Log("Saved backup schema to " + schemaBakPath);
            }

            Debug.LogFormat("Using ABR Schema, version {0}", SchemaJson["properties"]["version"]["default"]);

            var args = System.Environment.GetCommandLineArgs();
            for (var i = 1; i < args.Length; i++)
            {
                if (args[i].Equals("-ABRConfig"))
                {
                    abr_root = args[i+1];
                    break;
                }
            }

            if (abr_root == null)
            {
                abr_root = Environment.GetEnvironmentVariable("ABR_ROOT");
            }

            if (abr_root == null)
            {
                abr_root = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
            }

            
            string cfgFile = Path.Combine(abr_root, "abr.json");
            
            try
            {
                StreamReader reader = new StreamReader(cfgFile);
                string json = reader.ReadToEnd();
                ExternalConfiguration cfg = new ExternalConfiguration();
                cfg = JsonUtility.FromJson<ExternalConfiguration>(json);
                Debug.Log("Using external configuration file: " + cfgFile);

                serverUrl = cfg.serverURL;
                mediaPath = cfg.mediaDirectory;

                if (!System.IO.Path.IsPathRooted(mediaPath))
                {
                    mediaPath = Path.Combine(abr_root, mediaPath);
                }

                remotes = cfg.remotes;
                center = new Vector3(cfg.center.x, cfg.center.y, cfg.center.z);
                rotation = new Vector3(cfg.rotation.x, cfg.rotation.y, cfg.rotation.z);
                scale = cfg.scale;

                useAutoDataContainer = cfg.useAutoDataContainer;
                if (cfg.container != null)
                {
                    dataContainer.center = new Vector3(cfg.container.center.x, cfg.container.center.y, cfg.container.center.z);
                    dataContainer.extents = new Vector3(cfg.container.extent.x, cfg.container.extent.y, cfg.container.extent.z);
                }
            }
            catch (Exception e)
            {
                _ = e;
                Debug.Log("No external config file");
            }

        }

        public override string ToString()
        {
            JsonSerializerSettings settings = new JsonSerializerSettings();
            settings.ReferenceLoopHandling = ReferenceLoopHandling.Ignore;
            return JsonConvert.SerializeObject(this, Formatting.Indented, settings);
        }

        /// <summary>
        ///     Get the default primitive value for a particular data
        ///     impression's parameter
        /// </summary>
        public T GetInputValueDefault<T>(string plateName, string inputName)
        where T : IPrimitive
        {
            if (SchemaJson == null)
            {
                Debug.LogErrorFormat("Schema is null, cannot get default value {0}", inputName);
                return default(T);
            }
            string primitiveValue = SchemaJson["definitions"]["Plates"][plateName]["properties"][inputName]["properties"]["inputValue"]["default"].ToString();
            
            Type inputType = typeof(T);
            ConstructorInfo inputCtor =
                inputType.GetConstructor(
                    BindingFlags.Instance | BindingFlags.Public,
                    null,
                    CallingConventions.HasThis,
                    new Type[] { typeof(string) },
                    null
            );
            string[] args = new string[] { primitiveValue };
            try
            {
                T primitive = (T) inputCtor?.Invoke(args);
                return primitive;
            }
            catch (Exception)
            {
                Debug.LogErrorFormat("Unable to create primitive {0} using value `{1}`, using default value", inputType.ToString(), primitiveValue);
                return default(T);
            }
        }

        /// <summary>
        ///     Obtain a full list of all inputs available to this plate
        /// </summary>
        public string[] GetInputNames(string plateName)
        {
            if (SchemaJson == null)
            {
                Debug.LogErrorFormat("Schema is null, cannot get input names for {0}", plateName);
                return new string[0];
            }
            Dictionary<string, JToken> inputList = SchemaJson["definitions"]["Plates"][plateName]["properties"].ToObject<Dictionary<string, JToken>>();
            return inputList.Keys.ToArray();
        }


        /// <summary>
        /// Global access to constants in the ABR Engine
        /// </summary>
        public static class Consts
        {
            /// <summary>
            /// VisAsset folder within media folder
            /// </summary>
            public const string VisAssetFolder = "visassets";

            /// <summary>
            /// Dataset folder within media folder
            /// </summary>
            public const string DatasetFolder = "datasets";

            /// <summary>
            /// Default name for the media folder
            /// </summary>
            public const string MediaFolder = "media";

            /// <summary>
            /// Name of VisAsset JSON specifier
            /// </summary>
            public const string VisAssetJson = "artifact.json";
        }

        [System.Serializable]
        public class GroupToDataMatrixOverrideFields
        {
            [Header("Dataset path or Group name or UUID to affect (specify one)")]
            [Tooltip("Dataset path to affect with this matrix")]
            public string datasetPath;
            [Tooltip("Name of the group to affect with this matrix")]
            public string groupName;
            [Tooltip("UUID of the group to affect with this matrix")]
            public string groupUuid;
            [Tooltip("4x4 transformation matrix to modify the `GroupToDataMatrix` of this group")]
            public Matrix4x4 groupToDataMatrix = Matrix4x4.identity;
        }
    }
}
