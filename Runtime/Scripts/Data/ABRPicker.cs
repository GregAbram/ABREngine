using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;

namespace IVLab.ABREngine
{
    public class ABRPicker : MonoBehaviour
    {
        public List<GameObject> listeners;

        public int button = 1;
        public char modifier = 'n'; // n = none, c = ctrl, a = alt, s = shift

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

                if (! abrGO.TryGetComponent<IVLab.ABREngine.InstanceId>(out IVLab.ABREngine.InstanceId pickId))
                {
                    return;
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

            bool b = false;
#if ENABLE_INPUT_SYSEM
            switch (button)
            {
                case 0: b =  Mouse.current.leftButton.wasPressedThisFrame; break;
                case 1: b =  Mouse.current.rightButton.wasPressedThisFrame; break;
                case 2: b =  Mouse.current.middleButton.wasPressedThisFrame; break;
            }

            bool c = Keyboard.current.ctrlKey.isPressed;
            bool a = Keyboard.current.altKey.isPressed;
            bool s = Keyboard.current.shiftKey.isPressed;    
#else
            b = Input.GetMouseButtonDown(button);       
            bool c = Input.GetKey(KeyCode.LeftControl);
            bool a = Input.GetKey(KeyCode.LeftAlt);
            bool s = Input.GetKey(KeyCode.LeftShift);        
  
            
#endif
            if (b &&
                ((modifier == 'n' && !c && !a && !s) ||
                 (modifier == 'c' &&  c && !a && !s) ||
                 (modifier == 'a' && !c &&  a && !s) ||
                 (modifier == 's' && !c && !a &&  s))
                {
                    Raycast();
                }
            }
        }
    }
}




