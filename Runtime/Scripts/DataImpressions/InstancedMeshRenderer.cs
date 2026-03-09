/* KeyData.cs
 *
 * Copyright (c) 2020 University of Minnesota
 * Authors: Seth Johnson <sethalanjohnson@gmail.com>
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
// InstancedMeshRenderer.cs
// Drop-in replacement for your current script (keeps your public fields).
// Key fixes:
//  1) WORLD-SPACE bounds for DrawMeshInstancedProcedural (prevents whole-draw frustum culling)
//  2) Always uses transform.localToWorldMatrix / worldToLocalMatrix for _ObjectTransform(_Inverse)
//  3) Packs per-instance matrices into float4 rows (Vector4*4) to match the shader below
//  4) Correctly binds buffers + _InstanceCount/_UseInstanceBuffers every frame
using UnityEngine;
using UnityEngine.Rendering;
using System;

namespace IVLab.ABREngine
{
    public class InstancedMeshRenderer : MonoBehaviour
    {
        public Matrix4x4[] instanceLocalTransforms;
        public Vector4[]   renderInfo;
        public int[]       hiliteBuffer;

        public float instanceDensity  = -1.0f;
        public int   instanceCount    = 100000;
        public Mesh  instanceMesh;

        // Primary material (always used)
        public Material instanceMaterial;

        // Outline material - if set, a second draw call is issued with
        // Cull Front to render the outline. Set to null for no outline.
        public Material outlineMaterial;

        public int    subMeshIndex = 0;
        public Bounds bounds;

        public int  cachedInstanceCount = -1;
        private int cachedSubMeshIndex  = -1;

        private ComputeBuffer renderInfoBuffer;
        private ComputeBuffer transformBuffer;
        private ComputeBuffer transformBufferInverse;
        private ComputeBuffer perInstanceHiliteBuffer;

        private bool invalid    = true;
        public  bool buffersDirty = false;
        public  MaterialPropertyBlock block;

        public bool useInstanced = true;

        void Update()
        {
            if (cachedInstanceCount != instanceCount || cachedSubMeshIndex != subMeshIndex || buffersDirty)
            {
                buffersDirty = false;

                if (block == null)
                    block = new MaterialPropertyBlock();

                UpdateBuffers();
            }

            if (invalid) return;
            if (instanceMesh == null || instanceMaterial == null) return;

            instanceMaterial.enableInstancing = true;

            block.SetMatrix("_ObjectTransform",        transform.localToWorldMatrix);
            block.SetMatrix("_ObjectTransformInverse", transform.worldToLocalMatrix);
            block.SetInt("_UseInstanceBuffers", 1);
            block.SetInt("_InstanceCount",      instanceCount);

            var transformedBounds = new Bounds(transform.position, Vector3.one * 1000000f);

            if (useInstanced)
            {
                // Use outline material (back faces, expanded)
                // Only issued if an outline material has been assigned
                if (outlineMaterial != null)
                {
                    outlineMaterial.enableInstancing = true;
                    Graphics.DrawMeshInstancedProcedural(
                        instanceMesh,
                        subMeshIndex,
                        outlineMaterial,
                        transformedBounds,
                        instanceCount,
                        block,
                        ShadowCastingMode.Off,
                        false,
                        gameObject.layer
                    );
                }                
                
                // Primary draw call - main material (front faces, lit)
                Graphics.DrawMeshInstancedProcedural(
                    instanceMesh,
                    subMeshIndex,
                    instanceMaterial,
                    transformedBounds,
                    instanceCount,
                    block,
                    ShadowCastingMode.On,
                    true,
                    gameObject.layer
                );


            }
            else
            {
                // Non-instanced fallback
                for (int i = 0; i < instanceLocalTransforms.Length; i++)
                {
                    block.SetVector("_RenderInfo", renderInfo[i]);
                    Graphics.DrawMesh(
                        instanceMesh,
                        transform.localToWorldMatrix * instanceLocalTransforms[i],
                        instanceMaterial,
                        gameObject.layer,
                        null,
                        subMeshIndex,
                        block
                    );
                }
            }
        }

        void UpdateBuffers()
        {
            invalid = true;

            if (block == null) return;
            if (instanceLocalTransforms == null || instanceLocalTransforms.Length == 0) return;
            if (renderInfo == null || renderInfo.Length != instanceLocalTransforms.Length) return;
            if (instanceMesh == null || instanceMaterial == null) return;

            invalid       = false;
            instanceCount = instanceLocalTransforms.Length;

            if (instanceMesh != null)
                subMeshIndex = Mathf.Clamp(subMeshIndex, 0, instanceMesh.subMeshCount - 1);

            // Release old buffers
            renderInfoBuffer?.Release();
            transformBuffer?.Release();
            transformBufferInverse?.Release();
            perInstanceHiliteBuffer?.Release();

            // Create new buffers
            renderInfoBuffer       = new ComputeBuffer(instanceCount,     sizeof(float) * 4);
            transformBuffer        = new ComputeBuffer(instanceCount,     sizeof(float) * 16);
            transformBufferInverse = new ComputeBuffer(instanceCount,     sizeof(float) * 16);

            int hiliteInts          = (instanceCount + 31) / 32;
            perInstanceHiliteBuffer = new ComputeBuffer(hiliteInts, sizeof(Int32));

            if (hiliteBuffer == null || hiliteBuffer.Length != hiliteInts)
            {
                hiliteBuffer = new int[hiliteInts];
            }
            perInstanceHiliteBuffer.SetData(hiliteBuffer);

            // Build inverse array
            var inverses = new Matrix4x4[instanceCount];
            for (int i = 0; i < instanceCount; i++)
                inverses[i] = instanceLocalTransforms[i].inverse;

            transformBuffer.SetData(instanceLocalTransforms);
            transformBufferInverse.SetData(inverses);
            renderInfoBuffer.SetData(renderInfo);

            // Bind buffers to property block
            // Both the main material and outline material share the same block
            block.SetBuffer("transformBuffer",        transformBuffer);
            block.SetBuffer("transformBufferInverse", transformBufferInverse);
            block.SetBuffer("renderInfoBuffer",       renderInfoBuffer);
            block.SetBuffer("perInstanceHiliteBuffer", perInstanceHiliteBuffer);

            block.SetInt("_UseInstanceBuffers", 1);
            block.SetInt("_InstanceCount",      instanceCount);

            // Set sensible defaults in case UpdateStyling hasn't run yet
            block.SetColor("_Color",    Color.white);
            block.SetInt("_UseColorMap", 1);
            //block.SetFloat("_ColorDataMin", 0f);
            //block.SetFloat("_ColorDataMax", 1f);
            block.SetColor("_NaNColor",   Color.black);
            block.SetColor("_HiliteColor", Color.yellow);

            cachedInstanceCount = instanceCount;
            cachedSubMeshIndex  = subMeshIndex;
        }

        void OnDestroy()
        {
            renderInfoBuffer?.Release();
            transformBuffer?.Release();
            transformBufferInverse?.Release();
            perInstanceHiliteBuffer?.Release();
        }

        // --- Hilite API ---

        public void toggleHilite(int which)
        {
            if (hiliteBuffer == null) return;
            int arrayOffset = which / 32;
            int bitOffset   = which % 32;
            if (arrayOffset < 0 || arrayOffset >= hiliteBuffer.Length) return;
            hiliteBuffer[arrayOffset] ^= (1 << bitOffset);
            PushHiliteBufferToGPU();
        }

        public void setHilite(int which)
        {
            if (hiliteBuffer == null) return;
            int arrayOffset = which / 32;
            int bitOffset   = which % 32;
            if (arrayOffset < 0 || arrayOffset >= hiliteBuffer.Length) return;
            hiliteBuffer[arrayOffset] |= (1 << bitOffset);
            PushHiliteBufferToGPU();
        }

        public void clearHilite(int which)
        {
            if (hiliteBuffer == null) return;
            int arrayOffset = which / 32;
            int bitOffset   = which % 32;
            if (arrayOffset < 0 || arrayOffset >= hiliteBuffer.Length) return;
            hiliteBuffer[arrayOffset] &= ~(1 << bitOffset);
            PushHiliteBufferToGPU();
        }

        private void PushHiliteBufferToGPU()
        {
            if (perInstanceHiliteBuffer == null) return;
            int expected = (instanceCount + 31) / 32;
            if (hiliteBuffer == null || hiliteBuffer.Length != expected || perInstanceHiliteBuffer.count != expected)
            {
                UpdateBuffers();
                return;
            }
            perInstanceHiliteBuffer.SetData(hiliteBuffer);
            block?.SetBuffer("perInstanceHiliteBuffer", perInstanceHiliteBuffer);
        }
    }
}
