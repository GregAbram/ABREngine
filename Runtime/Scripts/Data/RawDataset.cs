/* RawDataset.cs
 *
 * Copyright (c) 2021 University of Minnesota
 * Authors: Bridger Herman <herma582@umn.edu>, Seth Johnson
 * <sethalanjohnson@gmail.com>
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
using System.IO;
using System.Collections.Generic;
using UnityEngine;
using System.Linq;
using System.Text.RegularExpressions;


namespace IVLab.ABREngine
{

    [System.Serializable]
    public class SerializableFloatArray
    {
        [SerializeField]
        public float[] array;
    }

    [System.Serializable]
    public class SerializableVectorArray
    {
        [SerializeField]
        public Vector3[] array;
    }

    /// <summary>
    ///     The raw variable arrays and geometry for a Data Object. RawDatasets
    ///     can be loaded from a pair of .json and .bin files (JsonHeader and
    ///     BinaryData, respectively). This RawDataset defines the specification
    ///     for each of these files. RawDataset is not to be confused with
    ///     `Dataset`, which represents a *collection* of RawDatasets which
    ///     share a coordinate space, key data, and variables.
    /// </summary>
    /// <example>
    /// A simple 4-vertex plane with no variables can be created like this:
    /// <code>
    /// RawDataset ds = new RawDataset();
    /// ds.meshTopology = MeshTopology.Triangles;
    /// ds.bounds = new Bounds(Vector3.zero, Vector3.one * 2.0f);
    ///
    /// ds.vectorArrays = new SerializableVectorArray[0];
    /// ds.vectorArrayNames = new string[0];
    /// ds.scalarArrays = new SerializableFloatArray[0];
    /// ds.scalarArrayNames = new string[0];
    /// ds.scalarMins = new float[0];
    /// ds.scalarMaxes = new float[0];
    ///
    /// // Construct the vertices
    /// Vector3[] vertices = {
    ///     new Vector3(-1, 0, -1), // 0
    ///     new Vector3( 1, 0, -1), // 1
    ///     new Vector3(-1, 0,  1), // 2
    ///     new Vector3( 1, 0,  1), // 3
    /// };
    ///
    /// ds.vertexArray = vertices;
    /// // Construct triangle indices/faces - LEFT HAND RULE, outward-facing normals
    /// int[] indices = {
    ///     // Bottom face
    ///     0, 1, 3,
    ///     0, 3, 2
    /// };
    ///
    /// ds.indexArray = indices;
    /// // How many verts per cell are there? (each triangle is a cell)
    /// int[] cellIndexCounts = { 3, 3 };
    /// ds.cellIndexCounts = cellIndexCounts;
    ///
    /// // Where does each cell begin?
    /// int[] cellIndexOffsets = { 0, 3 };
    /// ds.cellIndexOffsets = cellIndexOffsets;
    /// </code>
    /// </example>
    [System.Serializable]
    public class RawDataset
    {
        public string dataPath;
        
        [SerializeField]
        public Vector3[] vertexArray;

        [SerializeField]
        public SerializableVectorArray[] vectorArrays;

        [SerializeField]
        public string[] vectorArrayNames;

        [SerializeField]
        public SerializableFloatArray[] scalarArrays;

        // NOTE: Matrix arrays not yet supported in data format
        // Pending rewrite of data format.
        public Matrix4x4[][] matrixArrays;
        public string[] matrixArrayNames;

        [SerializeField]
        public string[] scalarArrayNames;

        [SerializeField]
        public float[] scalarMins;

        [SerializeField]
        public float[] scalarMaxes;

        [SerializeField]
        public int[] indexArray;

        [SerializeField]
        public int[] cellIndexOffsets;

        [SerializeField]
        public int[] cellIndexCounts;

        [SerializeField]
        public Bounds bounds;

        [SerializeField]
        public Vector3Int dimensions;

        int currentIndex = -1;

        public bool isRemote;

        /// <summary>
        /// Header that contains metadata for a particular RawDataset
        /// </summary>
        public class JsonHeader
        {
            public DataTopology meshTopology;
            public int num_points;
            public int num_cells;
            public int num_cell_indices;
            public string[] scalarArrayNames;
            public string[] vectorArrayNames;
            public Bounds bounds;
            public int[] dimensions;           
             public float[] origin;
            public float[] spacing;

            public float[] scalarMaxes;
            public float[] scalarMins;
            public float[] timesteps;
            public string[] timestepFiles;
            public bool isTimeVarying;
            public float minTime;
            public float maxTime;

        }

        public JsonHeader info;

        /// <summary>
        /// Actual geometric representation of the data to load from a file / socket
        /// </summary>
        public class BinaryData
        {
            public float[] vertices { get; set; }
            public int[] index_array { get; set; }
            public float[][] scalar_arrays { get; set; }
            public float[][] vector_arrays { get; set; }

            Vector3 translation;
            float scale;

            public void Decode(JsonHeader bdh, byte[] bytes)
            {
                int offset = 0;
                int nbytes;
                int num_points;

                // No vertices stored in binary for volumes
                if (bdh.meshTopology != DataTopology.Voxels)
                {
                    vertices = new float[3 * bdh.num_points];
                    nbytes = 3 * bdh.num_points * sizeof(float);
                    Buffer.BlockCopy(bytes, offset, vertices, 0, nbytes);
                    offset = offset + nbytes;
                    num_points = bdh.num_points;
                }
                else
                    num_points = bdh.dimensions[0] * bdh.dimensions[1] * bdh.dimensions[2];

                Vector3 center = ABREngine.Instance.Config.center;
                float scale = (float)ABREngine.Instance.Config.scale;

                for (int i = 0; i < 3 * bdh.num_points;)
                {
                    vertices[i] = (vertices[i] - center.x) * scale;
                    i++;
                    vertices[i] = (vertices[i] - center.y) * scale;
                    i++;
                    vertices[i] = (vertices[i] - center.z) * scale;
                    i++;
                }

                index_array = new int[bdh.num_cell_indices];
                nbytes = bdh.num_cell_indices * sizeof(int);
                Buffer.BlockCopy(bytes, offset, index_array, 0, nbytes);
                offset = offset + nbytes;

                scalar_arrays = new float[bdh.scalarArrayNames.Length][];
                nbytes = num_points * sizeof(float);
                for (int i = 0; i < bdh.scalarArrayNames.Length; i++)
                {
                    scalar_arrays[i] = new float[num_points];
                    Buffer.BlockCopy(bytes, offset, scalar_arrays[i], 0, nbytes);
                    offset = offset + nbytes;
                }

                vector_arrays = new float[bdh.vectorArrayNames.Length][];
                nbytes = 3 * num_points * sizeof(float);
                for (int j = 0; j < bdh.vectorArrayNames.Length; j++)
                {
                    vector_arrays[j] = new float[3 * num_points];
                    Buffer.BlockCopy(bytes, offset, vector_arrays[j], 0, nbytes);
                    for (int v = 0; v < num_points; v++)
                        vector_arrays[j][v * 3 + 2] = -vector_arrays[j][v * 3 + 2];
                    offset = offset + nbytes;
                }
            }

            public static byte[] Encode(JsonHeader bdh, in Vector3[] vertices, in int[] indices, in int[] cellIndexOffsets, in int[] cellIndexCounts, in SerializableFloatArray[] scalars, in SerializableVectorArray[] vectors)
            {
                // Convert everything to base types
                float[] verticesFloat = new float[vertices.Length * 3];
                for (int vert = 0; vert < vertices.Length; vert++)
                {
                    verticesFloat[vert * 3 + 0] = vertices[vert].x;
                    verticesFloat[vert * 3 + 1] = vertices[vert].y;
                    verticesFloat[vert * 3 + 2] = vertices[vert].z;
                }

                float[][] scalarArrays = new float[scalars.Length][];
                for (int scalarArray = 0; scalarArray < scalarArrays.Length; scalarArray++)
                {
                    scalarArrays[scalarArray] = scalars[scalarArray].array;
                }

                float[][] vectorArrays = new float[vectors.Length][];
                for (int vectorArray = 0; vectorArray < vectorArrays.Length; vectorArray++)
                {
                    int numVectorValues = vectors[vectorArray].array.Length;
                    vectorArrays[vectorArray] = new float[numVectorValues * 3];
                    for (int vector = 0; vector < numVectorValues; vector++)
                    {
                        vectorArrays[vectorArray][vector * 3 + 0] = vectors[vectorArray].array[vector].x;
                        vectorArrays[vectorArray][vector * 3 + 1] = vectors[vectorArray].array[vector].y;
                        vectorArrays[vectorArray][vector * 3 + 2] = vectors[vectorArray].array[vector].z;
                    }
                }

                // Calculate the ParaView-style indices from the current indices and cells

                // Unstructured indices have {# indices in cell1, idx1, idx2, idx3, #indices in cell2....}
                int[] unstructuredIndices = new int[bdh.num_cell_indices + bdh.num_cells];

                int ui = 0;
                for (int cellIndexInDataset = 0; cellIndexInDataset < bdh.num_cells; cellIndexInDataset++)
                {
                    // Set "count" for this cell
                    unstructuredIndices[ui++] = cellIndexCounts[cellIndexInDataset];

                    // Set index values for this cell
                    int indexOffset = cellIndexOffsets[cellIndexInDataset];
                    for (int indexInCell = 0; indexInCell < cellIndexCounts[cellIndexInDataset]; indexInCell++)
                    {
                        unstructuredIndices[ui++] = indices[indexOffset + indexInCell];
                    }
                }

                int offset = 0;

                int vertsByteLength = verticesFloat.Length * sizeof(float);
                int idxByteLength = bdh.num_cell_indices * sizeof(int);
                int scalarsByteLength = 0;
                int vectorsByteLength = 0;
                for (int i = 0; i < bdh.scalarArrayNames.Length; i++)
                    scalarsByteLength += scalarArrays[i].Length * sizeof(float);
                for (int i = 0; i < bdh.vectorArrayNames.Length; i++)
                    vectorsByteLength += vectorArrays[i].Length * sizeof(float); // vec3s are already converted to floats

                int outputNumBytes = vertsByteLength + idxByteLength + scalarsByteLength + vectorsByteLength;

                byte[] outBytes = new byte[outputNumBytes];

                // No vertices stored in binary for volumes
                if (bdh.meshTopology != DataTopology.Voxels)
                {
                    Buffer.BlockCopy(verticesFloat, 0, outBytes, offset, vertsByteLength);
                    offset = offset + vertsByteLength;
                }

                // Copy indices over
                Buffer.BlockCopy(unstructuredIndices, 0, outBytes, offset, idxByteLength);
                offset = offset + idxByteLength;

                // Copy variables over
                for (int i = 0; i < bdh.scalarArrayNames.Length; i++)
                {
                    int nbytes = scalarArrays[i].Length * sizeof(float);
                    Buffer.BlockCopy(scalarArrays[i], 0, outBytes, offset, nbytes);
                    offset = offset + nbytes;
                }
                for (int i = 0; i < bdh.vectorArrayNames.Length; i++)
                {
                    int nbytes = scalarArrays[i].Length * sizeof(float);
                    Buffer.BlockCopy(vectorArrays[i], 0, outBytes, offset, nbytes);
                    offset = offset + nbytes;
                }

                return outBytes;
            }

            public BinaryData(JsonHeader bdh, string file) {
                byte[] bytes = File.ReadAllBytes(file);
                Decode(bdh, bytes);
            }

            public BinaryData(JsonHeader bdh, byte[] bytes)
            {
                Decode(bdh, bytes);
            }
        }


        // This one is used when we are loading a dataset from the file system.
        // The path is relative to the media data folder.
        public static JsonHeader LoadHeaderLocal(string name)
        {
            string dataDir = Path.Combine(ABREngine.Instance.Config.abr_root, ABREngine.Instance.Config.mediaPath, ABRConfig.Consts.DatasetFolder);

            string[] parts = name.Split('/');

            for (int i = 0; i < parts.Length - 1; i++)
                dataDir = Path.Combine(dataDir, parts[i]);

            string dataName = parts[parts.Length - 1];
            string jsonPath = Path.Combine(dataDir, dataName) + ".json";
            
            if (! File.Exists(jsonPath))
                return null;
            
            JsonHeader hdr = LoadHeaderString(File.ReadAllText(jsonPath));

            string[] binFiles = Directory.GetFiles(dataDir, dataName + "-*.tstep");
            if (binFiles.Length > 0)
            {
                //binFiles = Directory.GetFiles(dataDir, dataName + "-*.tstep");                
                List<Tuple<double, string>> tupleList = new List<Tuple<double, string>>();

                for (int i = 0; i < binFiles.Length; i++)
                {
                    string binFile = binFiles[i];
                    string fname = Path.GetFileNameWithoutExtension(binFile);

                    if (fname.Length > 1)
                    {
                        string timestring = RSplit(fname, "-", 1)[1];
                        var c = Regex.Matches(timestring, @"\d?[\.\d?]*");

                        if (c.Count > 0)
                        {
                            double t = Convert.ToDouble(timestring);
                            tupleList.Add(Tuple.Create(t, binFile));
                        }
                    }
                }

                List<Tuple<double, string>> sortedList = tupleList.OrderBy(o => o.Item1).ToList();

                hdr.timestepFiles = new string[sortedList.Count];
                hdr.timesteps = new float[sortedList.Count];

                for (int i = 0; i < sortedList.Count; i++)
                {
                    hdr.timesteps[i] = (float)sortedList[i].Item1;
                    hdr.timestepFiles[i] = sortedList[i].Item2;
                }


                hdr.isTimeVarying = true;
                hdr.minTime = hdr.timesteps[0];
                hdr.maxTime = hdr.timesteps[hdr.timesteps.Length - 1];

                ABREngine.Instance.UpdateTimeRange(hdr.minTime, hdr.maxTime);
            }
            else
            {
                hdr.timestepFiles = new string[1];
                hdr.timesteps = new float[1];
                hdr.timestepFiles[0] = jsonPath;
                hdr.timesteps[0] = 0.0f;
                hdr.isTimeVarying = false;
            }

            return hdr; 
        }


        public static JsonHeader LoadHeaderRemote(string name)
        {
            ABRConfig config = ABREngine.Instance.Config;
            foreach (ABRConfig.RemoteDataSource remote in config.remotes)
            {  
#if false
                try
                {
                    MyStream ms = new MyStream(remote.host, remote.port);
                    ms.SendString("get header:" + name);
                    string rply = ms.ReadString();
                    if (rply == "yes")
                    {
                        string json = ms.ReadString();
                        return LoadHeaderString(json);
                    }
                }
                catch(Exception e)
                {
                    Debug.Log(e.ToString());
                }
#endif
            }
            return null;
        }
        public static JsonHeader LoadHeaderString(string bytes)
        {
            try {
                JsonHeader hdr = JsonUtility.FromJson<RawDataset.JsonHeader>(bytes);
            } 
            catch (Exception e)
            {
                Debug.LogFormat("failed to load JSON: {0}", e.Message);
            }
            return JsonUtility.FromJson<RawDataset.JsonHeader>(bytes);
        }
           
        public RawDataset() { }


        public RawDataset(JsonHeader jh)
        {
            info = jh;
        }

        public RawDataset(JsonHeader jh, byte[] bytes)
        {
            info = jh;

            LoadByteData(bytes);
        } 

        public bool UpdateTimestep()
        {
            float currentTime = ABREngine.Instance.GetCurrentTime();

            int ti = 0;
            for (ti = 0; ti < (info.timestepFiles.Length - 1); ti++)  
            {
                if (info.timesteps[ti+1] > currentTime)
                    break;
            }

            if (ti == currentIndex)
                return false;

            currentIndex = ti;

            string dataPath = info.timestepFiles[currentIndex];
            JsonHeader hdr = null;
            byte[] bytes = null;

            if (isRemote)
            {
#if false
                foreach (ABRConfig.RemoteDataSource remote in ABREngine.Instance.Config.remotes)
                {
                    MyStream ms = new(remote.host, remote.port);
                    ms.SendString("get data:" + dataPath);
                    string rply = ms.ReadString();
                    if (rply == "yes")
                    {
                        
                        hdr = LoadHeaderString(ms.ReadString());
                        bytes = ms.ReadBytes();
                    }
                }
#endif
            }
            else
            {
                string jsonFile = Path.Combine(ABREngine.Instance.Config.mediaPath, dataPath);
                string binFile = Path.ChangeExtension(jsonFile, "bin");
                hdr = LoadHeaderString(File.ReadAllText(jsonFile));
                bytes = File.ReadAllBytes(binFile);
            }
            LoadByteData(hdr, bytes);
            return true;
        }

        private void LoadByteData(byte[] bytes)
        {
            LoadByteData(info, bytes);
        }

        private void LoadByteData(JsonHeader info, byte[] bytes)
        {
            BinaryData bd = new BinaryData(info, bytes);

            // Convert the vertices. Volumes don't have vertices, they have dimensions instead (number of voxels in x y z)
            if (info.meshTopology == DataTopology.Voxels)
            {
                dimensions = new Vector3Int(info.dimensions[0], info.dimensions[1], info.dimensions[2]);
            }
            else
            {
                vertexArray = new Vector3[info.num_points];
                for (int i = 0; i < info.num_points; i++)
                {
                    vertexArray[i][0] = bd.vertices[i * 3 + 0];
                    vertexArray[i][1] = bd.vertices[i * 3 + 1];
                    vertexArray[i][2] = bd.vertices[i * 3 + 2];
                }
            }
            // Debug.Log("Loaded verts: " + string.Join(", ", vertexArray));

            // Determine how many indices are in the dataset.
            // If points or voxels, each point/voxel is a cell.
            // If surface or line, extract the proper number of indices from the number of cells. Incoming format is:
            // {# indices in cell 0, idx0, idx1, idx2, #indices in cell 1, idx0, idx1, idx2, ...}, for example on a cube made up of triangles:
            // 3, 0, 1, 2,     3, 3, 2, 1,     3, 4, 6, 5,     3, 7, 5, 6,     3, 8, 10, 9, ....
            long numIndices = 0;
            if (info.meshTopology == DataTopology.Points || info.meshTopology == DataTopology.Voxels)
                numIndices = info.num_cells;
            else
            {
                long indx = 0;
                for (int i = 0; i < info.num_cells; i++)
                {
                    long k = bd.index_array[indx];
                    numIndices = numIndices + bd.index_array[indx];
                    indx = indx + k + 1;
                }
            }
    
            cellIndexOffsets = new int[info.num_cells];
            cellIndexCounts = new int[info.num_cells];
            indexArray = new int[numIndices];

            int src_indx = 0;
            int dst_indx = 0;
            for (long c = 0; c < info.num_cells; c++)
            {
                cellIndexOffsets[c] = dst_indx;
                cellIndexCounts[c] = bd.index_array[src_indx];
                src_indx = src_indx + 1;

                for (long p = 0; p < cellIndexCounts[c]; p++)
                {
                    indexArray[dst_indx] = bd.index_array[src_indx];
                    src_indx++;
                    dst_indx++;
                }
            }

            bounds = info.bounds;
            scalarArrayNames = info.scalarArrayNames;
            vectorArrayNames = info.vectorArrayNames;
            scalarMins = info.scalarMins;
            scalarMaxes = info.scalarMaxes;

            scalarArrays = new SerializableFloatArray[info.scalarArrayNames.Count()];
            
            for (int i = 0; i < scalarArrayNames.Count(); i++)
            {
                scalarArrays[i] = new SerializableFloatArray();
                scalarArrays[i].array = new float[info.num_points];
                for (int j = 0; j < info.num_points; j++)
                    scalarArrays[i].array[j] = bd.scalar_arrays[i][j];
            }

            vectorArrays = new SerializableVectorArray[info.vectorArrayNames.Count()];
            for (int i = 0; i < info.vectorArrayNames.Count(); i++)
            {
                vectorArrays[i] = new SerializableVectorArray();
                vectorArrays[i].array = new Vector3[info.num_points];
                for (int j = 0; j < info.num_points; j++)
                {
                    vectorArrays[i].array[j][0] = bd.vector_arrays[i][j * 3 + 0];
                    vectorArrays[i].array[j][1] = bd.vector_arrays[i][j * 3 + 1];
                    vectorArrays[i].array[j][2] = bd.vector_arrays[i][j * 3 + 2];
                }
            }
        }

        /// <summary>
        /// Convert this raw dataset into a .json and .bin pair representation.
        /// Does not save the file, only returns a tuple.
        /// </summary>
        /// <returns>
        /// Returns a tuple (json data header, binary data file contents)
        /// </returns>
        public Tuple<string, byte[]> ToFilePair()
        {
            JsonHeader jh = new JsonHeader();
            jh.meshTopology = this.info.meshTopology;
            jh.num_points = this.vertexArray.Length;
            jh.num_cells = this.cellIndexCounts.Length;
            jh.num_cell_indices = this.cellIndexCounts.Sum() + this.cellIndexCounts.Length; // num_cell_indices actually includes the "counts" as well
            jh.bounds = this.bounds;
            jh.scalarArrayNames = this.scalarArrayNames;
            jh.vectorArrayNames = this.vectorArrayNames;
            jh.scalarMins = this.scalarMins;
            jh.scalarMaxes = this.scalarMaxes;
            jh.dimensions = new int[] { this.dimensions.x, this.dimensions.y, this.dimensions.z };

            string json = JsonUtility.ToJson(jh);
            byte[] binData = BinaryData.Encode(jh, this.vertexArray, this.indexArray, this.cellIndexOffsets, this.cellIndexCounts, this.scalarArrays, this.vectorArrays);

            return Tuple.Create(json, binData);
        }

        private Dictionary<string, int> _vectorDictionary;
        private Dictionary<string, int> vectorDictionary
        {
            get
            {
                if (_vectorDictionary == null)
                {
                    _vectorDictionary = new Dictionary<string, int>();
                    for (int i = 0; i < vectorArrayNames.Length; i++)
                    {
                        _vectorDictionary[vectorArrayNames[i]] = i;
                    }
                }
                return _vectorDictionary;
            }
        }

        public Vector3[] GetVectorArray(string name)
        {
            int index;

            if (vectorDictionary.TryGetValue(name, out index))
                return vectorArrays?[index].array;
            else return null;
        }

        private Dictionary<string, int> _scalarDictionary;
        private Dictionary<string, int> scalarDictionary
        {
            get
            {
                if (_scalarDictionary == null || _scalarDictionary.Count != scalarArrayNames.Length)
                {
                    _scalarDictionary = new Dictionary<string, int>();
                    for (int i = 0; i < scalarArrayNames.Length; i++)
                    {
                        _scalarDictionary[scalarArrayNames[i]] = i;
                    }
                }
                return _scalarDictionary;

            }
        }

        public bool HasScalarArray(string name)
        {
            return scalarDictionary.ContainsKey(name);
        }

        public bool HasVectorArray(string name)
        {
            return vectorDictionary.ContainsKey(name);
        }

        public float[] GetScalarArray(string name)
        {
            int index;

            if (scalarDictionary.TryGetValue(name, out index))
                return scalarArrays[index].array;
            else
                return null;

        }
        public float GetScalarMin(string name)
        {
            int index;
            scalarDictionary.TryGetValue(name, out index);
            return scalarMins[index];
        }

        public float GetScalarMax(string name)
        {
            int index;
            scalarDictionary.TryGetValue(name, out index);
            return scalarMaxes[index];
        }

        public Matrix4x4[] GetMatrixArray(string name)
        {
            int index = Array.IndexOf(matrixArrayNames, name);
            if (index > 0)
                return matrixArrays[index];
            else
                return null;

        }

        // TODO: Not implemented in the data schema yet
        public Vector3 GetVectorMin(string name)
        {
            // int index;
            // scalarDictionary.TryGetValue(name, out index);
            // return scalarMins[index];
            return Vector3.zero;
        }

        // TODO: Not implemented in the data schema yet
        public Vector3 GetVectorMax(string name)
        {
            // int index;
            // scalarDictionary.TryGetValue(name, out index);
            // return scalarMaxes[index];
            return Vector3.zero;
        }


        /// <summary>
        /// Splits a string from the right, similar to Python's rsplit.
        /// </summary>
        /// <param name="input">The string to split.</param>
        /// <param name="separator">The separator string.</param>
        /// <param name="count">Maximum number of splits from the right. If 0 or less, returns the whole string as one element.</param>
        /// <returns>Array of split parts.</returns>
        public static string[] RSplit(string input, string separator, int count)
        {
            if (input == null)
                throw new ArgumentNullException(nameof(input));
            if (separator == null)
                throw new ArgumentNullException(nameof(separator));
            if (count <= 0)
                return new[] { input };

            // Split from the left without limit
            string[] parts = input.Split(new string[] { separator }, StringSplitOptions.None);

            if (count >= parts.Length)
                return parts; // No need to merge

            // Merge the left part back so that only 'count' splits happen from the right
            int mergeCount = parts.Length - count;
            string[] result = new string[count + 1];
            result[0] = string.Join(separator, parts, 0, mergeCount);
            Array.Copy(parts, mergeCount, result, 1, count);

            return result;
        }
    }

}
