using Mirror;
using System.Collections;
using System.Collections.Generic;
using TMPro;
using UnityEngine;

public class MenuManager : MonoBehaviour
{
    [SerializeField] private TMP_InputField roomNameInput;
    [SerializeField] private NetworkManager networkManager;

    public void CreateRoom()
    {
        if (string.IsNullOrEmpty(roomNameInput.text)) return;

        networkManager.networkAddress = roomNameInput.text;
        networkManager.StartHost();
    }

    public void JoinRoom()
    {
        if (string.IsNullOrEmpty(roomNameInput.text)) return;

        // ��� ���������� ������������
        if (roomNameInput.text == "localhost")
            networkManager.networkAddress = "127.0.0.1";
        else
            networkManager.networkAddress = roomNameInput.text;

        networkManager.StartClient();

        // �������� �������
        StartCoroutine(ConnectionTimeout());
    }

    private IEnumerator ConnectionTimeout()
    {
        yield return new WaitForSeconds(5f);
        if (!NetworkClient.isConnected)
            Debug.LogError("�� ������� ������������!");
    }
}
