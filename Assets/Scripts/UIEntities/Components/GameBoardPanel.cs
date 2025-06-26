using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using UnityEngine;

namespace Assets.Scripts.UIEntities.Components
{
    public class GameBoardPanel : MonoBehaviour
    {
        private void OnTransformChildrenChanged()
        {
            foreach (Transform child in transform)
            {
                // Сброс поворота (разворачиваем карту)
                child.localRotation = Quaternion.identity; 
                
                // Сброс масштаба (возвращаем в норму)
                child.localScale = Vector3.one;
            }
        }
    }
}
