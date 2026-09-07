using DG.Tweening;
using System;
using System.Threading.Tasks;
using UnityEngine;
using UnityEngine.UI;

namespace Assets.Scripts.UIEntities
{
    /// <summary>
    /// Перелёт карты туда, где она останется лежать: движение, доводка позы и
    /// разворот по дороге.
    ///
    /// <para><b>Карта уходит в корень канваса и там остаётся.</b> Родителя ей
    /// назначает вызывающий — у каждого полёта своя посадка: одни проверяют,
    /// не уехала ли карта дальше за время пути, другие уничтожают её на месте.
    /// Уводить карту из панели обязательно: три раскладки и
    /// <c>GameBoardPanel</c> сами пишут детям позицию, поворот и масштаб, так
    /// что любой поворот на карте-ребёнке затрётся первой же пересборкой. Тот
    /// же приём применяет <see cref="DragAndDrop"/> на время перетаскивания.
    /// Заодно летящая карта рисуется поверх остального стола, а веер, из
    /// которого она вылетела, смыкается сразу, а не на посадке.</para>
    /// </summary>
    public static class CardFlight
    {
        // Насколько карта «приподнимается» к зрителю на пике разворота.
        // Канвас в игре — Screen Space - Overlay, перспективы там нет, и без
        // этого подъёма разворот читается как плоское сжатие по ширине, а не
        // как переворот карты в воздухе.
        private const float FlipLift = 0.08f;

        /// <summary>
        /// Ведёт карту к <paramref name="to"/> за <paramref name="duration"/>
        /// секунд, разворачивая её в конце пути.
        /// </summary>
        /// <param name="flipDuration">
        /// Сколько длится сам разворот. Он <b>вложен</b> в полёт, а не добавлен
        /// к нему: очередь событий ждёт анимаций стола, поэтому удлинение
        /// полёта замедлило бы всю партию.
        /// </param>
        /// <param name="onFlipMidpoint">
        /// Подмена лица в середине разворота, когда карта стоит ребром к
        /// зрителю. <c>null</c> — карта не переворачивается, только летит и
        /// приводится к конечной позе.
        /// </param>
        /// <returns>
        /// Долетела ли карта. <c>false</c> — полёт оборвали на полпути: карту
        /// уничтожили вместе со сценой либо её перехватил следующий полёт
        /// (см. <c>DOKill</c> ниже). <b>Сажать карту в этом случае нельзя</b> —
        /// вызывающий обязан просто уйти, иначе он назначит родителя и позу
        /// карте, которую уже ведёт кто-то другой.
        /// </returns>
        public static async Task<bool> Fly(CardView card, Transform to,
                                           float duration, float flipDuration,
                                           Action onFlipMidpoint)
        {
            if (card == null || to == null) return false;

            var t = card.transform;
            var root = t.root;
            if (root == t) return false; // карта вне канваса — лететь неоткуда и некуда

            // Гасим предыдущий полёт этой же карты. DOTween сам старый твин не
            // убирает: два полёта на одном трансформе тянут его в разные
            // стороны, и карта уезжает куда-то между. Так и выглядела карта
            // Волшебника — стол успевал её забрать и разложить, а
            // незавершённый полёт ещё полсекунды волок её прочь.
            t.DOKill();

            // Полёт, убитый ровно посередине разворота, оставил бы карту ребром
            // к зрителю — то есть невидимой. Разворачиваем её обратно, прежде
            // чем начать новый.
            t.rotation = Quaternion.Euler(0f, 0f, t.eulerAngles.z);

            // Дальше всё считаем в системе координат канваса: и старт, и
            // финиш карта проводит его прямым ребёнком, так что локальные
            // значения сравнимы между собой независимо от того, какой масштаб
            // был у панели, из которой карта вылетела.
            t.SetParent(root, true);
            var fromPosition = t.position;
            var fromRotation = t.rotation;
            var fromScale = t.localScale;

            ProbeLanding(t, to, root, out var toPosition, out var toRotation, out var toScale);

            t.position = fromPosition;
            t.rotation = fromRotation;
            t.localScale = fromScale;

            float flip = onFlipMidpoint == null ? 0f : Mathf.Clamp(flipDuration, 0f, duration);
            float flipStart = duration <= 0f ? 0f : 1f - flip / duration;
            float fromZ = fromRotation.eulerAngles.z;
            float toZ = toRotation.eulerAngles.z;
            bool swapped = false;
            bool completed = false;

            // Один твин на всю позу, а не три параллельных (DOMove + DORotate +
            // DOScale). Твины на одном трансформе в этой игре уже дважды
            // разъезжались друг с другом, а на границе половин разворота
            // добавилась бы ещё и зависимость от того, в каком порядке DOTween
            // снимает стартовые значения. Здесь позу пишет одно место.
            // Тип параметра указан явно: у DOTween.To есть перегрузки под
            // double и int, и без него разрешение перегрузок пришлось бы
            // выводить из тела лямбды.
            await DOTween.To(() => 0f, (float u) =>
            {
                if (card == null) return;

                t.position = Vector3.LerpUnclamped(
                    fromPosition, toPosition, DOVirtual.EasedValue(0f, 1f, u, Ease.OutQuad));

                if (flip <= 0f)
                {
                    // Без разворота поза доводится плавно за весь полёт: карта
                    // теряет наклон веера и масштаб руки по дороге, а не
                    // щелчком на посадке.
                    t.rotation = Quaternion.Slerp(fromRotation, toRotation, u);
                    t.localScale = Vector3.Lerp(fromScale, toScale, u);
                    return;
                }

                if (u < flipStart)
                {
                    // До разворота карта летит как есть, только подгоняя
                    // размер: наклон снимется в середине разворота, даром.
                    t.localScale = Vector3.Lerp(fromScale, toScale, Mathf.InverseLerp(0f, flipStart, u));
                    return;
                }

                float f = Mathf.InverseLerp(flipStart, 1f, u);
                float half = f * 180f;

                // 0 -> 90: карта поворачивается ребром к зрителю, её ширина
                // сходит на нет. Вторая половина идёт не 90 -> 180, а
                // -90 -> 0: на ортографическом канвасе угол за 90 градусов
                // просто отражает картинку, и лицо оказалось бы зеркальным.
                //
                // Наклон веера (а у верхних мест — ещё и разворот стула на 180
                // градусов, см. SeatLayout.Place) снимаем ровно на середине,
                // скачком. Карта в этот миг стоит ребром, её ширина — ноль, и
                // доворот на любой угол физически не виден. Иначе она либо
                // крутилась бы колесом всю дорогу, либо щёлкала на посадке.
                t.rotation = Quaternion.Euler(0f, half < 90f ? half : half - 180f, f < 0.5f ? fromZ : toZ);
                t.localScale = toScale * (1f + FlipLift * Mathf.Sin(f * Mathf.PI));

                if (!swapped && f >= 0.5f)
                {
                    swapped = true;
                    onFlipMidpoint();
                }
            }, 1f, duration)
                .SetEase(Ease.Linear)
                // Без цели твин не найдётся по transform.DOKill(), и гашение
                // старых полётов перестало бы работать — молча.
                .SetTarget(t)
                // OnComplete срабатывает только при честном финише: убитый
                // твин сюда не заходит. Иначе не отличить долетевшую карту от
                // перехваченной, а разница принципиальная — см. <returns>.
                .OnComplete(() => completed = true)
                .AsyncWaitForCompletion();

            // Полёт переживает перезагрузку сцены (выход в меню посреди
            // партии), а карта — нет. Уничтоженный объект Unity равен null по
            // своей перегрузке ==, но обращение к нему бросает
            // MissingReferenceException, а бросать его отсюда некуда: эти
            // задачи ждут из async void.
            if (card == null || !completed) return false;

            // Последний кадр твина мог не совпасть с единицей — доводим позу
            // до конца, чтобы карта не осталась приподнятой, недовёрнутой или
            // рубашкой вверх.
            t.position = toPosition;
            t.rotation = toRotation;
            t.localScale = toScale;
            if (!swapped && onFlipMidpoint != null) onFlipMidpoint();
            return true;
        }

        /// <summary>
        /// Поза, в которую раскладка назначения поставит карту: куда встанет,
        /// под каким углом и какого размера. Считается в системе координат
        /// канваса — там же, где идёт полёт.
        ///
        /// <para>Нужна, чтобы лететь сразу на своё место. Раньше карта летела
        /// в точку самой зоны, а раскладка на посадке мгновенно переставляла
        /// её в настоящий слот ряда или стопки — тем самым рывком, ради
        /// которого всё и затевалось.</para>
        ///
        /// <para>Способ узнать слот только один: отдать карту зоне и дать
        /// раскладке посчитать. Делается это <b>строго синхронно</b>, без
        /// единого <c>await</c>: промежуточное состояние не доживает до
        /// отрисовки, поэтому соседние карты не дёргаются туда-обратно — они
        /// подвинутся один раз, на посадке.</para>
        ///
        /// <para>Карта остаётся ребёнком канваса: там и пройдёт весь полёт.
        /// Вызывающий обязан вернуть ей стартовую позу — замер её сбивает.</para>
        /// </summary>
        private static void ProbeLanding(Transform t, Transform to, Transform root,
                                         out Vector3 position, out Quaternion rotation, out Vector3 scale)
        {
            position = to.position;
            rotation = t.rotation;
            scale = t.localScale;

            // Ни колода, ни слот дня/ночи, ни сброс раскладки не имеют: карта
            // ложится ровно в их точку, и мерить там нечего.
            var toRect = to as RectTransform;
            if (toRect == null || to.GetComponent<LayoutGroup>() == null) return;

            // Зона с нулевой шириной ещё не посчитана — так бывает, пока идёт
            // расстановка мест. Раскладка в этом случае сознательно ничего не
            // двигает (см. CardTableLayout.UpdateLayout), и замер вернул бы не
            // слот, а случайную позицию. Летим в точку зоны, как раньше.
            if (toRect.rect.width <= 0f) return;

            // worldPositionStays: false — панели повёрнуты к центру стола, и
            // сохранение мировой позы оставило бы карте компенсирующий
            // локальный трансформ. Он всё равно не переживёт пересборку: все
            // три раскладки пишут ребёнку и позицию, и поворот, и масштаб.
            t.SetParent(to, false);
            LayoutRebuilder.ForceRebuildLayoutImmediate(toRect);

            t.SetParent(root, true);
            position = t.position;
            rotation = t.rotation;
            scale = t.localScale;
        }
    }
}
