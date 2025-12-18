using System;
using System.Collections.Generic;
using UnityEngine;

namespace IVLab.ABREngine
{
    public class ABRPicker : MonoBehaviour
    {
        public List<GameObject> listeners;


        public struct ABRPick
        {
            public Vector3 point;
            public Vector3 barycentric_weights; 
            public GameObject abrGameObject;
            public Guid guid;
            public int id;
            public RawDataset dataset;
        };

        public class ABRPickHandler : MonoBehaviour
        {
            public virtual  void onPick(ABRPick pick) { Debug.Log("baseclass pick"); }
        }

        public void Raycast()
        {
            Ray ray = Camera.main.ScreenPointToRay(Input.mousePosition);
            if (Physics.Raycast(ray, out RaycastHit hit))
            {
                ABRPick abrPick = new ABRPick();
                abrPick.point = hit.point;        
                
                GameObject abrGO = hit.collider.gameObject;

                int id = -1;
                if (! abrGO.TryGetComponent<IVLab.ABREngine.InstanceId>(out IVLab.ABREngine.InstanceId pickId))
                {
                    id = -1;
                }

                if (abrGO.name.Contains("ABR Surface"))
                {
                    abrPick.abrGameObject = abrGO;
                    abrPick.id = hit.triangleIndex;
                    abrPick.barycentric_weights = hit.barycentricCoordinate;
                    
                }
                else if (abrGO.name.Contains("ABR Glyph"))
                {
                    abrPick.id = pickId.id;
                    abrPick.abrGameObject = abrGO.transform.parent.parent.gameObject;
                }      
                else if (abrGO.name.Contains("ABR Line"))
                {
                     abrPick.id = pickId.id;
                    abrPick.abrGameObject = abrGO.transform.parent.parent.gameObject;
                }
                else 
                {
                    Debug.Log("Its not an ABR object");
                    return;
                }

                if (! abrPick.abrGameObject.TryGetComponent<EncodedGameObject>(out EncodedGameObject ego))
                {
                    Debug.Log("picked an ABR object that does not have a EGO");
                    return;
                }

                abrPick.guid = ego.Uuid;
                
                IDataImpression idi = ABREngine.Instance.GetDataImpression(ego.Uuid);
                if (idi == null)
                {
                    Debug.Log("No data impression found for pick");
                    return;
                }

                if (ABREngine.Instance.Data.TryGetRawDataset(idi.GetKeyData()?.Path, out abrPick.dataset))
                {
                    Debug.Log("Picked dataset: " + abrPick.dataset.dataPath);
                }
                else
                {
                    Debug.Log("Picked dataset not found");
                }   

                if (listeners.Count > 0)
                    foreach (GameObject listener in listeners)
                    {
                        ABRPickHandler handler = listener.GetComponent<IVLab.ABREngine.ABRPicker.ABRPickHandler>();
                        if (handler != null) 
                            handler.onPick(abrPick);
                    }
            }        
        }

        void Update()
        {
            if (Input.GetMouseButtonDown(1)) // right mouse button
            {
                Raycast();
            }
        }
    }
}
