using Assets.Scripts.Views;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

public class DeckView : MonoBehaviour
{
    public static DeckView Instance { get; private set; }

    public Image targetImage; // ������ �� ��������� Image
    public Sprite newSprite;   // ������, ������� �� ������ ����������

    private void Awake()
    {

        if (Instance == null)
        {
            Instance = this;
        }
        else
        {
            Destroy(gameObject);
        }
    }
    public void UnsetSuit()
    {
        if (targetImage != null && newSprite != null)
        {
            targetImage.color = Color.black;
        }
        else
        {
            Debug.LogError("Target Image or New Sprite is not assigned!");
        }
    }
}
