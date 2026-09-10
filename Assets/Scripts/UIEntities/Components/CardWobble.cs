using UnityEngine;

/// <summary>
/// Лёгкая дрожь карты — так помечаются карты, которые прямо сейчас можно выбрать.
///
/// <para>Раньше выбор рисовался жёлтой рамкой, но на столе, где карты лежат
/// стопками и наползают друг на друга, рамка читается как часть оформления
/// самой карты, а не как «нажми на меня». Движение в статичной раскладке видно
/// боковым зрением и ни с чем не путается.</para>
///
/// <para>Компонент вешается кодом из <see cref="CardView.SetPickable"/> и дальше
/// живёт вместе с представлением: включён — карта дрожит, выключен — стоит ровно
/// там же, где стояла.</para>
/// </summary>
[DisallowMultipleComponent]
public class CardWobble : MonoBehaviour
{
    // Градус с небольшим в каждую сторону: заметно, но карта не пляшет и не
    // лезет углом на соседку в стопке.
    private const float AmplitudeDegrees = 1.2f;

    // Примерно три с половиной колебания в секунду — это дрожь, а не покачивание.
    private const float Speed = 22f;

    // Поворот, вокруг которого дрожим, — то есть настоящее место карты.
    private Quaternion _rest;

    // Последнее, что выставили сами. Нужно, чтобы отличить свой поворот от
    // чужого: см. LateUpdate.
    private Quaternion _applied;

    private float _phase;

    private void Awake()
    {
        // Разводим карты по фазе. Синхронная дрожь группы кандидатов читается
        // как дрожание всего экрана, а не как «вот эти карты можно выбрать».
        _phase = Mathf.Repeat(GetInstanceID() * 0.618f, Mathf.PI * 2f);
    }

    private void OnEnable()
    {
        _rest = transform.localRotation;
        _applied = _rest;
    }

    private void OnDisable()
    {
        // Возвращаем карту ровно туда, откуда начали дрожать: иначе она
        // осталась бы стоять на случайной фазе синуса.
        transform.localRotation = _rest;
    }

    private void LateUpdate()
    {
        // Поворот карты задаёт раскладка — веер руки, ровный ряд на столе, — и
        // делает это при пересборке, уже после LateUpdate. Если поворот сменился
        // не нами, значит раскладка пересобралась, и дрожать надо вокруг нового
        // положения, а не тянуть карту к запомненному старому.
        if (transform.localRotation != _applied)
            _rest = transform.localRotation;

        // Нескалированное время: дрожь ничем не управляет и не должна
        // останавливаться, если кто-то поставил Time.timeScale в ноль.
        float angle = Mathf.Sin(Time.unscaledTime * Speed + _phase) * AmplitudeDegrees;

        _applied = _rest * Quaternion.Euler(0f, 0f, angle);
        transform.localRotation = _applied;
    }
}
