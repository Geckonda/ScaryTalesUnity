using ScaryTales;
using ScaryTales.Abstractions;
using ScaryTales.Decisions;
using System;
using System.Collections.Generic;

namespace Assets.Scripts.Network
{
    /// <summary>
    /// Контекст только для чтения, собранный поверх клиентского зеркала.
    ///
    /// <para><b>Зачем.</b> Чтобы подсветить правило, доступное игроку прямо
    /// сейчас, клиенту надо ответить на тот же вопрос, что и серверу:
    /// <c>IRuleEffect.IsEffectAvailable</c>. Вопрос этот принимает
    /// <see cref="IGameContext"/>, которого у клиента нет. Можно было бы
    /// продублировать условия («есть доспех и сброс не пуст»), но такая копия
    /// разъедется с оригиналом при первой же правке правил — а разъехавшаяся
    /// подсветка врёт игроку. Поэтому клиент зовёт ТУ ЖЕ функцию, подсунув ей
    /// свой снимок мира.</para>
    ///
    /// <para><b>Чего здесь намеренно нет.</b> Колода, менеджер предметов,
    /// движок и router — на клиенте их не существует, и любой их вызов был бы
    /// ошибкой в логике, а не поводом что-то подставить. Они бросают
    /// <see cref="NotSupportedException"/>: проверка доступности их не
    /// трогает (проверено по всем четырём эффектам), а если однажды тронет —
    /// пусть это будет громкая ошибка в логе, а не тихо неверная подсветка.
    /// Зовущий ловит исключение и считает правило недоступным.</para>
    ///
    /// <para>Сервер остаётся единственной властью: он проверяет доступность
    /// заново, когда интент приходит.</para>
    /// </summary>
    public sealed class ClientRuleContext : IGameContext
    {
        private readonly ClientGameView _view;
        private readonly ClientGameState _state;

        public ClientRuleContext(ClientGameView view)
        {
            _view = view;
            _state = new ClientGameState(view);
        }

        public IGameState GameState => _state;
        public IGameBoard GameBoard => _view.Board;
        public List<Player> Players => _view.Players;

        public Deck Deck => throw Unavailable(nameof(Deck));
        public ItemManager ItemManager => throw Unavailable(nameof(ItemManager));
        public IGameManager GameManager => throw Unavailable(nameof(GameManager));
        public IDecisionRouter Router => throw Unavailable(nameof(Router));

        private static NotSupportedException Unavailable(string member) =>
            new NotSupportedException(
                $"ClientRuleContext.{member}: на клиенте этого нет. " +
                "Контекст годится только для IsEffectAvailable.");

        /// <summary>
        /// Состояние партии по данным зеркала. Всё, что меняет мир, здесь
        /// запрещено: этот контекст читают, а не играют им.
        /// </summary>
        private sealed class ClientGameState : IGameState
        {
            private readonly ClientGameView _view;

            public ClientGameState(ClientGameView view) => _view = view;

            public bool IsNight => _view.IsNight;
            public bool IsGameOver => false;
            public int TurnCount => _view.TurnCount;

            public Player GetCurrentPlayer() => _view.CurrentPlayer;
            public List<Player> GetPlayers() => _view.Players;
            public string GetTimeOfday() => IsNight ? "Ночь" : "День";

            public void NextTurn() => throw Mutation(nameof(NextTurn));
            public bool RemovePlayer(Player player) => throw Mutation(nameof(RemovePlayer));
            public void EndGame() => throw Mutation(nameof(EndGame));
            public void ToggleNightPhase() => throw Mutation(nameof(ToggleNightPhase));
            public void SetPhase(bool isNight) => throw Mutation(nameof(SetPhase));

            private static NotSupportedException Mutation(string member) =>
                new NotSupportedException(
                    $"ClientGameState.{member}: клиент не меняет состояние партии, " +
                    "оно приходит событиями с сервера.");
        }
    }
}
