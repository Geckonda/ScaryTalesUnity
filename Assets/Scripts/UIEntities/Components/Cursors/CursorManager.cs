using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using UnityEngine;

namespace Assets.Scripts.UIEntities.Components.Cursors
{
    public class CursorManager : MonoBehaviour
    {
        public static CursorManager Instance;

        [Header("Курсоры")]
        public Texture2D defaultCursor;
        public Texture2D pointerCursor;
        public Vector2 hotSpot = Vector2.zero;

        void Awake()
        {
            if (Instance == null)
            {
                Instance = this;
                DontDestroyOnLoad(gameObject);
                SetDefaultCursor();
            }
            else
            {
                Destroy(gameObject);
            }
        }

        public void SetDefaultCursor()
        {
            Cursor.SetCursor(defaultCursor, hotSpot, CursorMode.Auto);
            Cursor.visible = true;
            Cursor.lockState = CursorLockMode.None;
        }

        public void SetPointerCursor()
        {
            Cursor.SetCursor(pointerCursor, hotSpot, CursorMode.Auto);
        }
    }
}
