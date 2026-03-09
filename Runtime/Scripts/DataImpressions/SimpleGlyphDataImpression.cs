/* SimpleGlyphDataImpression.cs
 *
 * Copyright (c) 2021 University of Minnesota
 * Authors: Bridger Herman <herma582@umn.edu>, Seth Johnson <sethalanjohnson@gmail.com>
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
using System.Reflection;
using UnityEditor.SceneManagement;
using UnityEngine;


namespace IVLab.ABREngine
{

    class SimpleGlyphRenderInfo : IDataImpressionRenderInfo
    {
        public string dataPath {get; set;} 
        public Matrix4x4[] transforms;
        public Vector4[] scalars;
        public Bounds bounds;
        public Int32[] hiliteFlags;
    }

    /// <summary>
    /// A "Glyphs" data impression that uses hand-sculpted geometry to depict point data.
    /// </summary>
    /// <example>
    /// An example of creating a single glyph data impression and setting its colormap, color variable, and glyph could be:
    /// <code>
    /// SimpleGlyphDataImpression gi = new SimpleGlyphDataImpression();
    /// gi.keyData = points;
    /// gi.colorVariable = yAxis;
    /// gi.colormap = ABREngine.Instance.VisAssets.GetDefault&lt;ColormapVisAsset&gt;() as ColormapVisAsset;
    /// gi.glyph = glyph;
    /// ABREngine.Instance.RegisterDataImpression(gi);
    /// </code>
    /// </example>
    [ABRPlateType("Glyphs")]
    public class SimpleGlyphDataImpression : DataImpression, IDataImpression
    {
        [ABRInput("Key Data", "Key Data", UpdateLevel.Data)]
        public KeyData keyData;

        /// <summary>
        /// Colormap applied to the <see cref="colorVariable"/>. This example
        /// switches between a linear white-to-green colormap and a linear
        /// black-to-white colormap.
        ///
        /// <img src="../resources/api/SimpleGlyphDataImpression/colormap.gif"/>
        /// </summary>
        [ABRInput("Colormap", "Color", UpdateLevel.Style)]
        public IColormapVisAsset colormap;

        /// <summary>
        /// Override the color used for NaN values in this data impression. If
        /// not supplied, will use the <see cref="ABRConfig.defaultNanColor"/>.
        /// </summary>
        public IColormapVisAsset nanColor;

        /// <summary>
        /// Variable used to determine which glyph to render at which data
        /// values. This only has any effect if <see cref="glyph"/> is a <see
        /// cref="GlyphGradient"/>.
        /// </summary>
        [ABRInput("Glyph Variable", "Glyph", UpdateLevel.Style)]
        public ScalarDataVariable glyphVariable;

        /// <summary>
        /// What glyph(s) to apply to the dataset. This can also take a <see
        /// cref="GlyphGradient"/>. This example alternates between spherical
        /// and thin cylindrical glyphs.
        ///
        /// <img src="../resources/api/SimpleGlyphDataImpression/glyph.gif"/>
        /// </summary>
        [ABRInput("Glyph", "Glyph", UpdateLevel.Data)]
        public IGlyphVisAsset glyph;

        /// <summary>
        /// Adjust the size of the glyphs (in Unity-space meters).
        ///
        /// <img src="../resources/api/SimpleGlyphDataImpression/glyphSize.gif"/>
        /// </summary>
        [ABRInput("Glyph Size", "Glyph", UpdateLevel.Style)]
        public LengthPrimitive glyphSize;

        /// <summary>
        /// Tweak the density of glyphs - subsamples the existing glyphs uniformly.
        /// </summary>
        [ABRInput("Glyph Density", "Glyph", UpdateLevel.Style)]
        public PercentPrimitive glyphDensity;

        /// <summary>
        /// "Forward" direction that glyphs should point in.
        /// </summary>
        [ABRInput("Forward Variable", "Direction", UpdateLevel.Data)]
        public VectorDataVariable forwardVariable;

        /// <summary>
        /// "Up" direction that glyphs should point in.
        /// </summary>
        [ABRInput("Up Variable", "Direction", UpdateLevel.Data)]
        public VectorDataVariable upVariable;

        /// <summary>
        /// Level of detail to use for glyph rendering (higher number = lower
        /// level of detail; most glyphs have 3 LODs)
        /// </summary>
        public int glyphLod = 1;

        /// <summary>
        /// Use random forward/up directions when no Vector variables are
        /// applied for forward/up.
        /// </summary>
        public bool useRandomOrientation = true;

        /// <summary>
        /// Show/hide outline on this data impression
        ///
        /// <img src="../resources/api/SimpleGlyphDataImpression/showOutline.gif"/>
        /// </summary>
        public BooleanPrimitive showOutline;

        /// <summary>
        /// Width (in Unity world coords) of the outline
        ///
        /// <img src="../resources/api/SimpleGlyphDataImpression/outlineWidth.gif"/>
        /// </summary>
        public LengthPrimitive outlineWidth;

        /// <summary>
        /// Color of the outline
        ///
        /// <img src="../resources/api/SimpleGlyphDataImpression/outlineColor.gif"/>
        /// </summary>
        public Color outlineColor;

        /// <summary>
        /// Force the use of <see cref="outlineColor"/> even if there's a
        /// colormap applied to the data. This example alternates between a
        /// white-to-green linear colormap (false) and a solid purple-blue
        /// (true)
        ///
        /// <img src="../resources/api/SimpleGlyphDataImpression/forceOutlineColor.gif"/>
        /// </summary>
        public BooleanPrimitive forceOutlineColor;

        protected override string[] MaterialNames { get; } = { "ABRGlyphs", "ABRGlyphOutlines" };
        protected override string LayerName { get; } = "ABR_Glyph";

        protected float[] glyphMeshSizes;
        protected float glyphMeshScale = 1.0f;

        protected Vector3[] positions;

        /// <summary>
        ///     Construct a data impession with a given UUID. Note that this
        ///     will be called from ABRState and must assume that there's a
        ///     single string argument with UUID.
        /// </summary>
        public SimpleGlyphDataImpression(string uuid) : base(uuid) { }
        public SimpleGlyphDataImpression() : base() { }

        public override Dataset GetDataset()
        {
            return keyData?.GetDataset();
        }

        public override KeyData GetKeyData()
        {
            return keyData;
        }

        public override void ComputeGeometry()
        {
            if (keyData == null)
            {
                RenderInfo = new SimpleGlyphRenderInfo
                {
                    dataPath = "none",
                    transforms = new Matrix4x4[0],
                    scalars = new Vector4[0],
                    bounds = new Bounds(),
                    hiliteFlags = new int[0],
                };
            }
            else
            {
                RawDataset dataset;
                if (!ABREngine.Instance.Data.TryGetRawDataset(keyData?.Path, out dataset))
                {
                    return;
                }
                DataImpressionGroup group = ABREngine.Instance.GetGroupFromImpression(this);

                int numPoints = dataset.vertexArray.Length;

                // Compute positions for each point, in room (Unity) space
                positions = new Vector3[numPoints];
                for (int i = 0; i < numPoints; i++)
                {
                    positions[i] = group.GroupToDataMatrix * dataset.vertexArray[i].ToHomogeneous();
                }

                // Get up and forwards vectors at each point
                Vector3[] dataForwards = null;
                Vector3[] dataUp = null;
                if (forwardVariable != null && forwardVariable.IsPartOf(keyData))
                {
                    dataForwards = forwardVariable.GetArray(keyData);
                }
                else
                {
                    var rand = new System.Random(0);
                    dataForwards = new Vector3[numPoints];
                    for (int i = 0; i < numPoints; i++)
                    {
                        if (useRandomOrientation)
                            dataForwards[i] = new Vector3(
                                (float)rand.NextDouble() * 2 - 1,
                                (float)rand.NextDouble() * 2 - 1,
                                (float)rand.NextDouble() * 2 - 1);
                        else
                            dataForwards[i] = Vector3.forward;
                    }
                }

                if (upVariable != null && upVariable.IsPartOf(keyData))
                {
                    dataUp = upVariable.GetArray(keyData);
                }
                else
                {
                    var rand = new System.Random(1);
                    dataUp = new Vector3[numPoints];
                    for (int i = 0; i < numPoints; i++)
                    {
                        if (useRandomOrientation)
                            dataUp[i] = new Vector3(
                                (float)rand.NextDouble() * 2 - 1,
                                (float)rand.NextDouble() * 2 - 1,
                                (float)rand.NextDouble() * 2 - 1);
                        else
                            dataUp[i] = Vector3.up;
                    }
                }

                // Compute orientations for each point
                Quaternion[] orientations = new Quaternion[numPoints];
                if (upVariable != null && forwardVariable != null)
                { // Treat up as the more rigid constraint
                    for (int i = 0; i < numPoints; i++)
                    {
                        Vector3 rightAngleForward = Vector3.Cross(
                            Vector3.Cross(dataUp[i], dataForwards[i]).normalized,
                            dataUp[i]
                        ).normalized;

                        Quaternion orientation = Quaternion.LookRotation(rightAngleForward, dataUp[i]) * Quaternion.Euler(0, 180, 0);
                        orientations[i] = orientation;
                    }
                }
                else // Treat forward as the more rigid constraint
                {
                    for (int i = 0; i < numPoints; i++)
                    {
                        Vector3 rightAngleUp = Vector3.Cross(
                            Vector3.Cross(dataForwards[i], dataUp[i]).normalized,
                            dataForwards[i]
                        ).normalized;

                        Quaternion orientation = Quaternion.LookRotation(dataForwards[i], rightAngleUp) * Quaternion.Euler(0, 180, 0);
                        orientations[i] = orientation;
                    }
                }

                var encodingRenderInfo = new SimpleGlyphRenderInfo()
                {
                    dataPath = keyData?.Path,
                    transforms = new Matrix4x4[numPoints],
                    scalars = new Vector4[numPoints]
                };

                int flagBufferSize = (numPoints / 32) + 1;

                encodingRenderInfo.hiliteFlags = new Int32[flagBufferSize];
                for (int i  = 0; i < flagBufferSize; i++)
                    encodingRenderInfo.hiliteFlags[i] = 0;

                // Get glyph scale and apply to instance mesh renderer transform
                ABRConfig config = ABREngine.Instance.Config;
                string plateType = this.GetType().GetCustomAttribute<ABRPlateType>().plateType;
                glyphMeshScale = config.GetInputValueDefault<LengthPrimitive>(plateType, "Glyph Size").Value;

                for (int i = 0; i < numPoints; i++)
                {
                    encodingRenderInfo.transforms[i] = Matrix4x4.TRS(positions[i], orientations[i], Vector3.one * glyphMeshScale);
                }

                // Apply room-space bounds to renderer
                encodingRenderInfo.bounds = group.GroupBounds;
                RenderInfo = encodingRenderInfo;
            }
        }

        bool first = true;

        public override void SetupGameObject(EncodedGameObject currentGameObject)
        {
            if (currentGameObject == null)
                return;
  
            if (!first) return;
            first = false;

            base.SetupGameObject(currentGameObject);

            GameObject renderers = new GameObject("Glyph Renderers");
            renderers.transform.SetParent(currentGameObject.transform, false);

            GameObject colliders = new GameObject("Glyph Colliders");
            colliders.transform.SetParent(currentGameObject.gameObject.transform, false);
            
            var SSrenderData = RenderInfo as SimpleGlyphRenderInfo;

            // Ensure there's an ABR layer for this object
            int layerID = LayerMask.NameToLayer(LayerName);
            if (layerID >= 0)
            {
                currentGameObject.gameObject.layer = layerID;
            }
            else
            {
                Debug.LogWarningFormat("Could not find layer {0} for SimpleGlyphDataImpression", LayerName);
            }

            // Return all previous renderers and colliders to pool
            for (int i = renderers.transform.childCount - 1; i >= 0; i--)
            {
                var child = renderers.transform.GetChild(i).gameObject;
                UnityEngine.Object.Destroy(child);
            }  

            for (int i = colliders.transform.childCount - 1; i >= 0; i--)
            {
                var child = colliders.transform.GetChild(i).gameObject;
                UnityEngine.Object.Destroy(child);
            }  

            // Create pooled game objects with mesh renderer and instanced mesh renderer
            int rendererCount = glyph?.VisAssetCount - 1 ?? 0;
            for (int stopIndex = -1; stopIndex < rendererCount; stopIndex++)
            {
                GameObject childRenderer = new GameObject();
                childRenderer.name = "Glyph Renderer Object " + stopIndex;

                // Parent the glyph renderer to this Data Impression and ensure that it's centered correctly
                // Unsure why necessary...
                // See also: PrepareImpression method of DataImpressionGroup class
                childRenderer.transform.SetParent(renderers.transform, false);
                childRenderer.transform.localPosition = Vector3.zero;
                childRenderer.transform.localRotation = Quaternion.identity;

                // Add mesh renderers to child object
                MeshRenderer mr = null;
                InstancedMeshRenderer imr = null;
                if (!childRenderer.TryGetComponent<MeshRenderer>(out mr))
                {
                    mr = childRenderer.gameObject.AddComponent<MeshRenderer>();
                }
                //DEBUG
                mr.enabled = false;
                if (!childRenderer.TryGetComponent<InstancedMeshRenderer>(out imr))
                {
                    imr = childRenderer.gameObject.AddComponent<InstancedMeshRenderer>();
                }

                // Setup instanced rendering based on computed geometry
                if (SSrenderData == null)
                {
                    Debug.LogWarning($"Instanced mesh renderer for glyph impression {this.Uuid} (stop {stopIndex}) is null, skipping");
                    return;
                }

                imr.bounds = SSrenderData.bounds;
                imr.instanceMaterial = ImpressionMaterials[0];
                imr.block = new MaterialPropertyBlock();
                imr.buffersDirty = true;
            }                
        }
 
        public override void UpdateStyling(EncodedGameObject currentGameObject)
        {
            Debug.Log($"UpdateStyling called frame {Time.frameCount}");

            // Default to using every transform in the data (re-populate and discard old transforms)
            var SSrenderData = RenderInfo as SimpleGlyphRenderInfo;

            GameObject renderers = currentGameObject.transform.Find("Glyph Renderers").gameObject;
            if (renderers == null)
                return;

            // Rescale the glyphs depending on their current "Glyph Size" input
            ABRConfig config = ABREngine.Instance.Config;
            string plateType = this.GetType().GetCustomAttribute<ABRPlateType>().plateType;
            glyphMeshScale = glyphSize?.Value ?? config.GetInputValueDefault<LengthPrimitive>(plateType, "Glyph Size").Value;
            glyphMeshSizes = new float[renderers.transform.childCount];

            for (int glyphIndex = 0; glyphIndex < renderers.transform.childCount; glyphIndex++)
            {
                 // Exit immediately if the game object or instanced mesh renderer relevant to this
                // impression do not yet exist
                InstancedMeshRenderer imr = renderers?.transform.GetChild(glyphIndex).GetComponent<InstancedMeshRenderer>();
                if (imr == null)
                    continue;                
                    
                Debug.Log($"showOutline={showOutline?.Value} ImpressionMaterials.Length={ImpressionMaterials.Length}");
                Debug.Log($"imr.instanceMaterial={imr.instanceMaterial?.name} imr.outlineMaterial={imr.outlineMaterial?.name}");

                imr.instanceLocalTransforms = SSrenderData.transforms;
                imr.renderInfo = SSrenderData.scalars;               
                imr.hiliteBuffer = SSrenderData.hiliteFlags;

                // Set up outline, if present
                imr.instanceMaterial = ImpressionMaterials[0];
                imr.outlineMaterial  = (showOutline != null && showOutline.Value) ? ImpressionMaterials[1] : null;

                // Determine the number of points / glyphs via the number of transforms the
                // instanced mesh renderer is currently tracking
                int numPoints = imr.instanceLocalTransforms.Length;

                // We might as well exit if there are no glyphs to update
                if (numPoints <= 0)
                    continue;

                // Create a new MaterialPropertyBlock for this specific glyph
                MaterialPropertyBlock block = new MaterialPropertyBlock();

                // However, don't waste time rescaling the glyphs if the scale hasn't actually changed
                // (If at some point we are no longer scaling all glyphs evenly and equally, this trick
                // to determine if the scale changed will likely no longer function correctly)
                float prevGlyphScale = imr.instanceLocalTransforms[0].GetColumn(0).magnitude;
                if (!Mathf.Approximately(prevGlyphScale, glyphMeshScale))
                {
                    for (int i = 0; i < imr.instanceLocalTransforms.Length; i++)
                    {
                        imr.instanceLocalTransforms[i] *= Matrix4x4.Scale(Vector3.one * glyphMeshScale / prevGlyphScale);
                    }
                }

                // Update the instanced mesh renderer to use the currently selected glyph
                if (glyph != null && glyph.GetMesh(glyphIndex, glyphLod) != null)
                {
                    imr.instanceMesh = glyph.GetMesh(glyphIndex, glyphLod);
                    block.SetTexture("_Normal", glyph.GetNormalMap(glyphIndex, glyphLod));
                }
                else
                {
                    Mesh mesh = ABREngine.Instance.Config.defaultGlyph.GetComponent<MeshFilter>().sharedMesh;
                    imr.instanceMesh = mesh;
                }

                glyphMeshSizes[glyphIndex] = imr.instanceMesh.bounds.size.magnitude;

                // Initialize "render info" -- stores scalar values and info on whether
                // or not glyphs should be rendered
                Vector4[] glyphRenderInfo = new Vector4[numPoints];

                // Re-sample based on glyph density, if it has changed
                float glyphDensityOut = glyphDensity?.Value ??
                    config.GetInputValueDefault<PercentPrimitive>(plateType, "Glyph Density").Value;
                    
                glyphDensityOut = Mathf.Clamp01(glyphDensityOut);
                if (imr.instanceDensity != glyphDensityOut || RenderHints.DataChanged)
                {
                    // Sample number of glyphs based on density
                    int sampleSize = (int)(numPoints * glyphDensityOut);
                    SampleGlyphs(glyphRenderInfo, sampleSize);


                }
                // If the glyph density hasn't changed, use the previous sample of glyphs
                else if (imr.renderInfo?.Length == glyphRenderInfo.Length)
                {
                    glyphRenderInfo = imr.renderInfo;
                }

                if (colorVariable != null && colorVariable.IsPartOf(keyData))
                {
                    var colorScalars = colorVariable.GetArray(keyData);
                    for (int i = 0; i < numPoints; i++)
                    {
                        glyphRenderInfo[i][0] = colorScalars[i];
                    }
                }

                if (glyphVariable != null && glyphVariable.IsPartOf(keyData))
                {
                    var glyphScalars = glyphVariable.GetArray(keyData);
                    for (int i = 0; i < numPoints; i++)
                    {
                        glyphRenderInfo[i][1] = glyphScalars[i];
                    }
                }

                // Get keydata-specific range, if there is one
                float colorVariableMin = 0.0f;
                float colorVariableMax = 0.0f;
                if (colorVariable != null && colorVariable.IsPartOf(keyData))
                {
                    if (colorVariable.SpecificRanges.ContainsKey(keyData.Path))
                    {
                        colorVariableMin = colorVariable.SpecificRanges[keyData.Path].min;
                        colorVariableMax = colorVariable.SpecificRanges[keyData.Path].max;
                    }
                    else
                    {
                        colorVariableMin = colorVariable.Range.min;
                        colorVariableMax = colorVariable.Range.max;
                    }
                }

Debug.Log($"renderInfo[0]: {glyphRenderInfo[0]}");
Debug.Log($"colorVariableMin={colorVariableMin} colorVariableMax={colorVariableMax}");
Debug.Log($"colormap null={colormap==null} gradient null={colormap?.GetColorGradient()==null}");

                // Apply and pack scalar variables
                // INDEX 0: Color
                if (colorVariable != null && colorVariable.IsPartOf(keyData))
                {
                    var colorScalars = colorVariable.GetArray(keyData);
                    for (int i = 0; i < numPoints; i++)
                    {
                        glyphRenderInfo[i][0] = colorScalars[i];
                    }
                }

                // INDEX 1: Glyph
                if (glyphVariable != null && glyphVariable.IsPartOf(keyData))
                {
                    var glyphScalars = glyphVariable.GetArray(keyData);
                    for (int i = 0; i < numPoints; i++)
                    {
                        glyphRenderInfo[i][1] = glyphScalars[i];
                    }
                }

                // Apply scalar/density changes to the instanced mesh renderer
                imr.instanceDensity = glyphDensityOut;
                imr.renderInfo = glyphRenderInfo;
          
                // Apply changes to the mesh's shader / material
                block.SetFloat("_ColorDataMin", colorVariableMin);
                block.SetFloat("_ColorDataMax", colorVariableMax);
                block.SetColor("_Color", ABREngine.Instance.Config.defaultColor);
                block.SetColor("_OutlineColor", outlineColor);
                block.SetFloat("_OutlineWidth", outlineWidth?.Value ?? 0.02f);
                block.SetInt("_ForceOutlineColor", (forceOutlineColor?.Value ?? false) ? 1 : 0);
                block.SetColor("_HiliteColor" , ABREngine.Instance.Config.hiliteColor);                
                block.SetFloat("_OutlineWidth", 0.02f); // hardcoded for testing


                if (colormap?.GetColorGradient() != null)
                {
                    block.SetInt("_UseColorMap", 1);
                    block.SetTexture("_ColorMap", colormap?.GetColorGradient());
                    block.SetColor("_NaNColor", nanColor?.GetColorGradient().GetPixel(0, 0) ?? ABREngine.Instance.Config.defaultNanColor);
                }
                else
                {
                    block.SetInt("_UseColorMap", 0);
                }

                imr.block = block;

                imr.cachedInstanceCount = -1;      
            }

            GameObject colliders = currentGameObject.transform.Find("Glyph Colliders").gameObject;
            if (colliders != null)
            {
                colliders.name = "colliders being deleted";
                UnityEngine.Object.Destroy(colliders);
            }          
            

            colliders = new GameObject("Glyph Colliders");
            colliders.transform.SetParent(currentGameObject.transform, false);
            
            if (positions != null)
            {
                for (int i = 0; i < positions.Length; i++)
                {
                    Vector3 p = positions[i];
                    GameObject colliderObj = new GameObject();
                    colliderObj.transform.SetPositionAndRotation(Vector3.zero, Quaternion.identity);
                    colliderObj.transform.localScale = Vector3.one;
                    colliderObj.name = "ABR Glyph Collider " + i;
                                        
                    SphereCollider collider = colliderObj.AddComponent<SphereCollider>();

                    collider.radius = glyphMeshSizes[0] * glyphMeshScale * 0.2f;  // diameter to radius, then smaller still
                    collider.center = p;
                    colliderObj.transform.SetParent(colliders.transform, false);

                    InstanceId instanceId = colliderObj.AddComponent<InstanceId>();
                    instanceId.id = i;
                };                
            }

            return;      
        }

        public override void UpdateVisibility(EncodedGameObject currentGameObject)
        {
            currentGameObject.gameObject.SetActive(RenderHints.Visible);
            return;
        }

        public override void Cleanup(EncodedGameObject currentGameObject)
        {
            base.Cleanup(currentGameObject);

            GameObject renderers = currentGameObject.transform.Find("Glyph Renderers").gameObject;
            if (renderers == null)
                return;

            // Return all previous renderers to pool
            for (int i = renderers.transform.childCount - 1; i >= 0; i--)
            {
                var child = renderers.transform.GetChild(i).gameObject;
                UnityEngine.Object.Destroy(child);
            }  
        }

        // Samples k glyphs, modifying glyph render info so that only they will be rendered
        // Uses reservoir sampling: (https://www.geeksforgeeks.org/reservoir-sampling/)
        private void SampleGlyphs(Vector4[] glyphRenderInfo, int k)
        {
            // Total number of glyphs
            int n = glyphRenderInfo.Length;

            // Index for elements in renderInfo
            int i;

            // Indices into renderInfo array for the glyphs that have been selected
            int[] idxReservoir = new int[k];


            // Select first k glyphs to begin
            for (i = 0; i < k; i++)
            {
                idxReservoir[i] = i;
                glyphRenderInfo[i][3] = 1;  // render the glyph
            }

            // Iterate through the remaining glyphs
            for (; i < n; i++)
            {
                int j = UnityEngine.Random.Range(0, i + 1);
                // Replace previous selections if the randomly
                // picked index is smaller than k
                if (j < k)
                {
                    glyphRenderInfo[idxReservoir[j]][3] = -1;  // discard the glyph
                    idxReservoir[j] = i;
                    glyphRenderInfo[i][3] = 1;  // render the glyph
                }
                // Otherwise unselect the glyph
                else
                {
                    glyphRenderInfo[i][3] = -1;  // discard the glyph
                }
            }
        }

        public void toggleHilite(EncodedGameObject currentGameObject, int which)
        {
            GameObject renderers = currentGameObject.transform.Find("Glyph Renderers").gameObject;
            if (renderers == null)
                return;

            for (int glyphIndex = 0; glyphIndex < renderers.transform.childCount; glyphIndex++)
            {
                // Exit immediately if the game object or instanced mesh renderer relevant to this
                // impression do not yet exist
                InstancedMeshRenderer imr = renderers?.transform.GetChild(glyphIndex).GetComponent<InstancedMeshRenderer>();
                if (imr == null)
                    continue;
            
                imr.toggleHilite(which);
            }

            RenderHints.StyleChanged = true;
        }        
        
        public void setHilite(EncodedGameObject currentGameObject, int which)
        {
            GameObject renderers = currentGameObject.transform.Find("Glyph Renderers").gameObject;
            if (renderers == null)
                return;

            for (int glyphIndex = 0; glyphIndex < renderers.transform.childCount; glyphIndex++)
            {
                // Exit immediately if the game object or instanced mesh renderer relevant to this
                // impression do not yet exist
                InstancedMeshRenderer imr = renderers?.transform.GetChild(glyphIndex).GetComponent<InstancedMeshRenderer>();
                if (imr == null)
                    continue;
            
                imr.setHilite(which);
            }

            RenderHints.StyleChanged = true;
        }

        public void clearHilite(EncodedGameObject currentGameObject, int which)
        {
            GameObject renderers = currentGameObject.transform.Find("Glyph Renderers").gameObject;
            if (renderers == null)
                return;

            for (int glyphIndex = 0; glyphIndex < renderers.transform.childCount; glyphIndex++)
            {
                // Exit immediately if the game object or instanced mesh renderer relevant to this
                // impression do not yet exist
                InstancedMeshRenderer imr = renderers?.transform.GetChild(glyphIndex).GetComponent<InstancedMeshRenderer>();
                if (imr == null)
                    continue;
            
                imr.clearHilite(which);
            }

            RenderHints.StyleChanged = true;
        }
    }
}   
