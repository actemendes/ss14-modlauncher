namespace SS14LocalMods.ChemMaster;

public static class Text
{
    public static string T(string ru, string en) => Environment.GetEnvironmentVariable("SS14_MOD_LANGUAGE") == "en" ? en : ru;
    public static string State(ExecutionState state) => state switch
    {
        ExecutionState.Ready => T("Готов к запуску", "Ready"),
        ExecutionState.Running => T("Выполнение", "Running"),
        ExecutionState.Waiting => T("Пауза между командами", "Waiting between commands"),
        ExecutionState.AwaitingState => T("Ожидание состава", "Awaiting inventory"),
        ExecutionState.AwaitingBeaker => T("Ожидание смены мензурки", "Awaiting beaker replacement"),
        ExecutionState.Paused => T("На паузе", "Paused"),
        ExecutionState.Completed => T("Готово", "Completed"),
        ExecutionState.Stopped => T("Остановлено", "Stopped"),
        _ => T("Ошибка", "Failed")
    };
}
