using System.Globalization;
using System.IO;
using System.Runtime.CompilerServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Media;

namespace ArcTrellis.App;

public static class Loc
{
    private static readonly Dictionary<string, string> Ru = new(StringComparer.Ordinal)
    {
        ["File"] = "Файл", ["New from Template…"] = "Создать из шаблона…", ["Open…"] = "Открыть…",
        ["Save As…"] = "Сохранить как…", ["Save as reusable Template…"] = "Сохранить как шаблон…",
        ["Import Markdown…"] = "Импортировать Markdown…", ["Export"] = "Экспорт", ["Microsoft Word (.docx)…"] = "Microsoft Word (.docx)…",
        ["Markdown (.md)…"] = "Markdown (.md)…", ["Timeline CSV (.csv)…"] = "Хронология CSV (.csv)…", ["Scrivener project folder…"] = "Папка проекта Scrivener…",
        ["Print Timeline…"] = "Печать хронологии…", ["Exit"] = "Выход", ["Edit"] = "Правка", ["Undo"] = "Отменить", ["Redo"] = "Повторить",
        ["Add Chapter"] = "Добавить главу", ["Add Plotline"] = "Добавить сюжетную линию", ["Add Scene"] = "Добавить сцену",
        ["View"] = "Вид", ["Light theme"] = "Светлая тема", ["Dark theme"] = "Тёмная тема",
        ["Language"] = "Язык", ["English"] = "English", ["Russian"] = "Русский", ["Help"] = "Справка", ["User Guide"] = "Руководство пользователя", ["About ArcTrellis"] = "О программе ArcTrellis",
        ["＋ Scene"] = "＋ Сцена", ["Save"] = "Сохранить", ["Story planning studio"] = "Студия планирования историй",
        ["Dashboard"] = "Обзор", ["Timeline"] = "Хронология", ["Outline"] = "План", ["Scenes"] = "Сцены", ["Characters"] = "Персонажи",
        ["Places"] = "Места", ["Notes"] = "Заметки", ["Relationships"] = "Связи", ["Search"] = "Поиск", ["Series View"] = "Серия",
        ["Series overview"] = "Обзор серии", ["Title"] = "Название", ["Author"] = "Автор", ["Genre"] = "Жанр", ["Premise / series description"] = "Замысел / описание серии",
        ["Books in this series"] = "Книги серии", ["Add book"] = "Добавить книгу", ["Delete book"] = "Удалить книгу", ["Writing progress"] = "Прогресс написания",
        ["Choose color"] = "Выбрать цвет", ["Red"] = "Красный", ["Green"] = "Зелёный", ["Blue"] = "Синий",
        ["Edit book"] = "Редактировать книгу", ["Subtitle"] = "Подзаголовок", ["Book goal"] = "Цель книги", ["Book updated"] = "Книга обновлена",
        ["Current words"] = "Написано слов", ["Series goal"] = "Цель серии", ["At a glance"] = "Сводка", ["Local-first"] = "Только локально",
        ["Your project stays in the file you choose. ArcTrellis creates rotating local backups and an autosave recovery copy. No account or network connection is used."] = "Проект хранится в выбранном вами файле. ArcTrellis создаёт локальные резервные копии и файл автовосстановления. Учётная запись и подключение к сети не используются.",
        ["Book:"] = "Книга:", ["Plotline:"] = "Сюжетная линия:", ["＋ Plotline"] = "＋ Линия", ["Delete plotline"] = "Удалить линию", ["＋ Chapter"] = "＋ Глава",
        ["Zoom −"] = "Масштаб −", ["Zoom +"] = "Масштаб +", ["Rename selected plotline"] = "Переименовать выбранную сюжетную линию", ["Color in #RRGGBB format"] = "Цвет в формате #RRGGBB",
        ["Delete"] = "Удалить", ["Chapter / beat details"] = "Сведения о главе / событии", ["Section / act:"] = "Раздел / акт:", ["Words:"] = "Слов:",
        ["Scene card"] = "Карточка сцены", ["Status"] = "Статус", ["Point of view"] = "Точка зрения", ["Setting"] = "Место действия", ["Word count"] = "Количество слов",
        ["Tags (comma separated)"] = "Теги (через запятую)", ["Summary"] = "Краткое описание", ["Scene notes / draft"] = "Заметки / черновик сцены", ["Editing notes"] = "Редакторские заметки",
        ["Custom fields"] = "Дополнительные поля", ["Field"] = "Поле", ["Value"] = "Значение", ["＋ Character"] = "＋ Персонаж", ["＋ Place"] = "＋ Место", ["＋ Note"] = "＋ Заметка",
        ["Story bible entry"] = "Запись энциклопедии", ["Name"] = "Имя", ["Category"] = "Категория", ["One-line summary"] = "Краткое резюме", ["Details"] = "Подробности",
        ["Image file path (optional)"] = "Путь к изображению (необязательно)", ["Custom sheet fields"] = "Дополнительные поля анкеты", ["＋ Relationship"] = "＋ Связь",
        ["Character & world connections"] = "Связи персонажей и мира", ["From"] = "От", ["Relationship"] = "Тип связи", ["To"] = "К", ["Description"] = "Описание",
        ["Search the entire series bible"] = "Поиск по всей энциклопедии серии", ["Type"] = "Тип", ["Context"] = "Контекст", ["Story spine"] = "Основа сюжета",
        ["Start a story project"] = "Создание проекта", ["Choose a working example or begin with a blank timeline. Everything can be edited later."] = "Выберите готовый пример или начните с пустой хронологии. Всё можно изменить позже.",
        ["Cancel"] = "Отмена", ["Create project"] = "Создать проект", ["Includes five editable starter templates"] = "Включает пять редактируемых шаблонов",
        ["Blank project"] = "Пустой проект", ["One book, one chapter, and a main plotline."] = "Одна книга, одна глава и основная сюжетная линия.", ["Blank"] = "Пустой",
        ["The Glass Horizon (worked example)"] = "Стеклянный горизонт (готовый пример)", ["A populated science-fantasy example with two plotlines, scenes, characters, places, notes, and a relationship."] = "Заполненный пример научного фэнтези с двумя сюжетными линиями, сценами, персонажами, местами, заметками и связью.",
        ["Example"] = "Пример", ["Three-act story"] = "Трёхактная история", ["A flexible beginning, confrontation, and resolution structure with eight guiding scene cards."] = "Гибкая структура начала, противостояния и развязки с восемью карточками-подсказками.",
        ["Structure"] = "Структура", ["Mystery investigation"] = "Детективное расследование", ["Track the crime, clues, competing theories, reversal, and reveal across parallel plotlines."] = "Отслеживайте преступление, улики, версии, поворот и раскрытие по параллельным сюжетным линиям.",
        ["Relationship arc"] = "Арка отношений", ["Plan two viewpoints through connection, friction, vulnerability, rupture, and chosen future."] = "Спланируйте две точки зрения через сближение, трения, уязвимость, разрыв и выбранное будущее.",
        ["Three-book series roadmap"] = "План серии из трёх книг", ["A high-level series view with a book for setup, expansion, and culmination."] = "Общий план серии: завязка, расширение и кульминация по отдельной книге.", ["Series"] = "Серия",
        ["Planned"] = "Запланировано", ["Drafted"] = "Черновик", ["Revised"] = "Отредактировано", ["Final"] = "Готово", ["Cut"] = "Удалено",
        ["PLOTLINE / CHAPTER"] = "СЮЖЕТНАЯ ЛИНИЯ / ГЛАВА", ["Drop a scene here or double-click"] = "Перетащите сцену сюда или щёлкните дважды",
        ["Ready"] = "Готово", ["Book added"] = "Книга добавлена", ["Book deleted"] = "Книга удалена", ["Chapter added"] = "Глава добавлена", ["Chapter deleted"] = "Глава удалена",
        ["Chapter reordered"] = "Порядок глав изменён", ["Plotline added"] = "Сюжетная линия добавлена", ["Plotline deleted"] = "Сюжетная линия удалена", ["Scene added"] = "Сцена добавлена",
        ["Scene deleted"] = "Сцена удалена", ["Scene moved"] = "Сцена перемещена", ["Relationship added"] = "Связь добавлена", ["Item deleted"] = "Элемент удалён", ["Character added"] = "Персонаж добавлен", ["Place added"] = "Место добавлено", ["Note added"] = "Заметка добавлена",
        ["Undid last structural change"] = "Последнее структурное изменение отменено", ["Redid change"] = "Изменение повторено", ["Local project • autosave enabled"] = "Локальный проект • автосохранение включено", ["Last changed:"] = "Последнее изменение:",
        ["New Book"] = "Новая книга", ["New Chapter"] = "Новая глава", ["New Scene"] = "Новая сцена", ["New Item"] = "Новый элемент", ["New Character"] = "Новый персонаж", ["New Place"] = "Новое место", ["New Note"] = "Новая заметка",
        ["Untitled Series"] = "Безымянная серия", ["Book One"] = "Книга 1", ["Chapter 1"] = "Глава 1", ["Act I"] = "Акт I", [" Template"] = " Шаблон",
        ["Book {0}"] = "Книга {0}", ["Chapter {0}"] = "Глава {0}", ["Plotline {0}"] = "Сюжетная линия {0}", ["Scene {0}"] = "Сцена {0}",
        ["Main Plot"] = "Основной сюжет", ["General"] = "Общее", ["Research"] = "Исследования", ["Related to"] = "Связан с",
        ["Book"] = "Книга", ["Chapter"] = "Глава", ["Scene"] = "Сцена", ["Character"] = "Персонаж", ["Place"] = "Место", ["Note"] = "Заметка",
        ["Open ArcTrellis project"] = "Открыть проект ArcTrellis", ["Save ArcTrellis project"] = "Сохранить проект ArcTrellis", ["Save reusable template"] = "Сохранить шаблон",
        ["Choose a folder for the Scrivener project"] = "Выберите папку для проекта Scrivener", ["Recover project"] = "Восстановление проекта", ["Import failed"] = "Ошибка импорта", ["Export failed"] = "Ошибка экспорта",
        ["Template save failed"] = "Ошибка сохранения шаблона", ["Delete this {0}?"] = "Удалить: {0}?", ["Save changes before closing?"] = "Сохранить изменения перед закрытием?",
        ["book and all of its scenes"] = "книгу и все её сцены", ["chapter and all of its scenes"] = "главу и все её сцены", ["plotline (its scenes will move to another plotline)"] = "сюжетную линию (её сцены будут перемещены в другую линию)", ["scene"] = "сцену", ["character"] = "персонажа", ["place"] = "место", ["note"] = "заметку",
        ["This project has unsaved changes. Continue and discard them?"] = "В проекте есть несохранённые изменения. Продолжить и отменить их?", ["A newer local autosave exists. Recover it?"] = "Найдена более новая локальная автокопия. Восстановить её?",
        ["Could not open the project.\n\n{0}"] = "Не удалось открыть проект.\n\n{0}", ["Could not save the project.\n\n{0}"] = "Не удалось сохранить проект.\n\n{0}",
        ["Autosaved locally at {0}"] = "Локально сохранено автоматически в {0}", ["Autosave failed: {0}"] = "Ошибка автосохранения: {0}", ["Blank project ready"] = "Пустой проект готов",
        ["Opened {0}"] = "Открыт файл {0}", ["Saved {0}"] = "Сохранён файл {0}", ["Reusable template saved"] = "Шаблон сохранён", ["Custom template created from {0}"] = "Пользовательский шаблон на основе {0}",
        ["Exported {0}"] = "Экспортирован файл {0}", ["Scrivener project exported"] = "Проект Scrivener экспортирован",
        ["ArcTrellis projects (*.arctrellis)|*.arctrellis|All files (*.*)|*.*"] = "Проекты ArcTrellis (*.arctrellis)|*.arctrellis|Все файлы (*.*)|*.*", ["ArcTrellis project (*.arctrellis)|*.arctrellis"] = "Проект ArcTrellis (*.arctrellis)|*.arctrellis",
        ["ArcTrellis template (*.json)|*.json"] = "Шаблон ArcTrellis (*.json)|*.json", ["Markdown files (*.md;*.markdown)|*.md;*.markdown|Text files (*.txt)|*.txt"] = "Файлы Markdown (*.md;*.markdown)|*.md;*.markdown|Текстовые файлы (*.txt)|*.txt",
        ["Word document (*.docx)|*.docx"] = "Документ Word (*.docx)|*.docx", ["Markdown (*.md)|*.md"] = "Документ Markdown (*.md)|*.md", ["CSV spreadsheet (*.csv)|*.csv"] = "Таблица CSV (*.csv)|*.csv", ["New project — ArcTrellis"] = "Новый проект — ArcTrellis",
        ["A private, local-first visual story planner for Windows.\nNo cloud account, tracking, or network connection required."] = "Приватный локальный визуальный планировщик историй для Windows.\nОблачная учётная запись, отслеживание и подключение к сети не требуются.",
        ["{0} result(s)"] = "Результатов: {0}", ["{0} book(s)"] = "Книг: {0}", ["{0} chapter(s)"] = "Глав: {0}", ["{0} scene card(s)"] = "Карточек сцен: {0}", ["{0} plotline(s)"] = "Сюжетных линий: {0}",
        ["{0} character(s)"] = "Персонажей: {0}", ["{0} place(s)"] = "Мест: {0}", ["{0} scenes drafted"] = "Сцен в работе: {0}", ["{0:N0} / {1:N0} words ({2:0}%)"] = "{0:N0} / {1:N0} слов ({2:0}%)"
    };

    private static readonly Dictionary<string, string> En = Ru.GroupBy(x => x.Value).ToDictionary(x => x.Key, x => x.First().Key, StringComparer.Ordinal);
    private static readonly ConditionalWeakTable<DependencyObject, OriginalText> Originals = new();
    private static readonly string SettingsPath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "ArcTrellis", "language.txt");
    public static string Language { get; private set; } = "en-US";
    public static bool IsRussian => Language.StartsWith("ru", StringComparison.OrdinalIgnoreCase);

    public static void Initialize()
    {
        try { Language = File.Exists(SettingsPath) ? File.ReadAllText(SettingsPath).Trim() : (CultureInfo.CurrentUICulture.Name.StartsWith("ru", StringComparison.OrdinalIgnoreCase) ? "ru-RU" : "en-US"); }
        catch { Language = "en-US"; }
        ApplyCulture();
    }

    public static void SetLanguage(string language)
    {
        Language = language.StartsWith("ru", StringComparison.OrdinalIgnoreCase) ? "ru-RU" : "en-US";
        ApplyCulture();
        try { Directory.CreateDirectory(Path.GetDirectoryName(SettingsPath)!); File.WriteAllText(SettingsPath, Language); } catch { }
    }

    public static string T(string? text)
    {
        if (string.IsNullOrEmpty(text)) return text ?? string.Empty;
        if (IsRussian) return Ru.TryGetValue(text, out var translated) ? translated : text;
        return En.TryGetValue(text, out var english) ? english : text;
    }

    public static string F(string format, params object[] args) => string.Format(CultureInfo.CurrentCulture, T(format), args);

    public static void Apply(DependencyObject root)
    {
        var visited = new HashSet<DependencyObject>();
        Visit(root, visited);
    }

    private static void Visit(DependencyObject item, HashSet<DependencyObject> visited)
    {
        if (!visited.Add(item)) return;
        var original = Originals.GetValue(item, _ => new OriginalText());
        switch (item)
        {
            case Window window when !BindingOperations.IsDataBound(window, Window.TitleProperty): original.Title ??= window.Title; window.Title = T(original.Title); break;
            case TextBlock text when !BindingOperations.IsDataBound(text, TextBlock.TextProperty):
                original.Text ??= text.Text;
                text.Text = TranslateStableText(original.Text, text.Text);
                break;
            case DataGridColumn dataColumn when dataColumn.Header is string dataHeader && !BindingOperations.IsDataBound(dataColumn, DataGridColumn.HeaderProperty): original.Header ??= dataHeader; dataColumn.Header = T(original.Header); break;
            case GridViewColumn gridColumn when gridColumn.Header is string gridHeader && !BindingOperations.IsDataBound(gridColumn, GridViewColumn.HeaderProperty): original.Header ??= gridHeader; gridColumn.Header = T(original.Header); break;
            case HeaderedItemsControl items when items.Header is string itemsHeader && !BindingOperations.IsDataBound(items, HeaderedItemsControl.HeaderProperty): original.Header ??= itemsHeader; items.Header = T(original.Header); break;
            case HeaderedContentControl headered when headered.Header is string contentHeader && !BindingOperations.IsDataBound(headered, HeaderedContentControl.HeaderProperty): original.Header ??= contentHeader; headered.Header = T(original.Header); break;
            case ContentControl content when content.Content is string value && !BindingOperations.IsDataBound(content, ContentControl.ContentProperty): original.Content ??= value; content.Content = T(original.Content); break;
        }
        if (item is FrameworkElement element && element.ToolTip is string tip) { original.ToolTip ??= tip; element.ToolTip = T(original.ToolTip); }
        int count = 0;
        try { count = VisualTreeHelper.GetChildrenCount(item); } catch { }
        for (int i = 0; i < count; i++) Visit(VisualTreeHelper.GetChild(item, i), visited);
        foreach (object child in LogicalTreeHelper.GetChildren(item)) if (child is DependencyObject dependency) Visit(dependency, visited);
        // Some custom control templates and unopened tabs are not represented by the
        // same WPF logical/visual route. Walk their owned content explicitly as well.
        if (item is Panel panel) foreach (UIElement child in panel.Children) Visit(child, visited);
        if (item is Decorator { Child: { } decoratedChild }) Visit(decoratedChild, visited);
        if (item is ContentControl { Content: DependencyObject contentObject }) Visit(contentObject, visited);
        if (item is ItemsControl itemControl) foreach (object child in itemControl.Items) if (child is DependencyObject dependency) Visit(dependency, visited);
        if (item is DataGrid dataGrid) foreach (var column in dataGrid.Columns) Visit(column, visited);
        if (item is ListView { View: GridView gridView }) foreach (var column in gridView.Columns) Visit(column, visited);
    }

    private static string TranslateStableText(string? original, string? current)
    {
        if (string.IsNullOrEmpty(original)) return current ?? string.Empty;
        current ??= string.Empty;
        // Labels are translated from their first-seen value. Computed text (for
        // example the dashboard statistics) is rendered again for each culture
        // and must not be replaced with a cached, whole multiline value.
        bool isKnownLabelState = string.Equals(current, original, StringComparison.Ordinal)
            || (Ru.TryGetValue(original, out string? russian) && string.Equals(current, russian, StringComparison.Ordinal))
            || (En.TryGetValue(original, out string? english) && string.Equals(current, english, StringComparison.Ordinal));
        return isKnownLabelState ? T(original) : current;
    }

    private static void ApplyCulture()
    {
        var culture = CultureInfo.GetCultureInfo(Language);
        CultureInfo.DefaultThreadCurrentCulture = culture;
        CultureInfo.DefaultThreadCurrentUICulture = culture;
        Thread.CurrentThread.CurrentCulture = culture;
        Thread.CurrentThread.CurrentUICulture = culture;
    }

    private sealed class OriginalText
    {
        public string? Title { get; set; }
        public string? Text { get; set; }
        public string? Header { get; set; }
        public string? Content { get; set; }
        public string? ToolTip { get; set; }
    }
}

public sealed class SceneStatusOption : ArcTrellis.Core.Models.ObservableObject
{
    private string _label;
    public SceneStatusOption(string code, string label) { Code = code; _label = label; }
    public string Code { get; }
    public string Label { get => _label; private set => Set(ref _label, value); }
    public void RefreshLocalization() => Label = Loc.T(Code);
}

public sealed class StatusTextConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture) => value is string status ? Loc.T(status) : value;
    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) => Binding.DoNothing;
}

public sealed class NonNegativeIntegerConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture) => value?.ToString() ?? "0";

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        => int.TryParse(value?.ToString(), out int number) && number >= 0 ? number : 0;
}
