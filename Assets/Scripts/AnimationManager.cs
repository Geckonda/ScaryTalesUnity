using System.Collections.Generic;
using System.Threading.Tasks;
using UnityEngine;

/// <summary>
/// Класс AnimationManager управляет анимациями в игре, отслеживая активные анимационные задачи.
/// Позволяет регистрировать анимации и ожидать завершения всех текущих анимаций.
/// </summary>
public class AnimationManager : MonoBehaviour
{
    /// <summary>
    /// Список активных анимационных задач.
    /// </summary>
    private readonly List<Task> _activeAnimations = new();

    /// <summary>
    /// Статический экземпляр AnimationManager.
    /// </summary>
    public static AnimationManager Instance { get; private set; }

    /// <summary>
    /// Метод вызывается при инициализации объекта. Устанавливает Instance и гарантирует,
    /// что только один экземпляр AnimationManager будет существовать в игре.
    /// </summary>
    private void Awake()
    {
        if (Instance != null && Instance != this)
        {
            Destroy(gameObject);
            return;
        }
        Instance = this;
        DontDestroyOnLoad(gameObject);
    }

    /// <summary>
    /// Регистрирует анимационную задачу, добавляя её в список активных анимаций.
    /// После завершения анимации задача будет удалена из списка.
    /// </summary>
    /// <param name="animationTask">Задача, представляющая анимацию.</param>
    public void Register(Task animationTask)
    {
        _activeAnimations.Add(animationTask);
        animationTask.ContinueWith(t => _activeAnimations.Remove(animationTask));
    }

    /// <summary>
    /// Ожидает завершения всех текущих активных анимаций.
    /// </summary>
    /// <returns>Задача, представляющая ожидание завершения всех анимаций.</returns>
    public async Task WaitForAllAnimations()
    {
        await Task.WhenAll(_activeAnimations.ToArray());
    }
}
