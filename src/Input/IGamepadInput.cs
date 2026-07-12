namespace GHelper.Linux.Input;

// Raw gamepad sink; GamepadNav routes here instead of focus-nav while captured.
public interface IGamepadInput
{
    // pressed: down=true, release=false.
    void GamepadButton(GamepadInputButton button, bool pressed);

    // Held axes, -1/0/1 (up/left negative).
    void GamepadDirection(int x, int y);
}

public enum GamepadInputButton { South, East, North, West }
