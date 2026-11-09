using HhAutoApplier.Domain.Resumes;
using HhAutoApplier.Domain.Searches;
using HhAutoApplier.Domain.Services;
using HhAutoApplier.Domain.Vacancies;

namespace HhAutoApplier.Domain.Applications;

// Агрегат: отклик пользователя на вакансию — оценка, резюме, письмо, анкета и путь по статусам до отклика с историей.
// Вакансия общая (карточка с агрегатора), отклик — у каждого пользователя свой
public class JobApplication
{
    // Для EF
    private JobApplication()
    {
    }

    public int Id { get; private set; }

    public string VacancyId { get; private set; } = "";

    // Карточка вакансии — только для чтения: правила оценки и промпты LLM смотрят на неё
    public Vacancy Vacancy { get; private set; } = null!;

    // Владелец отклика — владелец поиска, который нашёл вакансию; null — владелец по умолчанию, пока нет пользователей
    public Guid? UserId { get; private set; }

    // Поиск владельца, нашедший вакансию: его правила — для оценки и анкеты. null — поиск удалён
    public int? SearchId { get; private set; }

    public SearchProfile? Search { get; private set; }

    public DateTimeOffset CreatedAt { get; private set; }

    public VacancyStatus Status { get; private set; }

    public int Score { get; private set; }

    public string? StatusReason { get; private set; }

    public string? ResumeProfile { get; private set; }

    public DateTimeOffset? AppliedAt { get; private set; }

    public LlmVerdict? LlmVerdict { get; private set; }

    public int? LlmScore { get; private set; }

    public string? LlmReason { get; private set; }

    public string? LlmModel { get; private set; }

    public DateTimeOffset? LlmEvaluatedAt { get; private set; }

    // Сопроводительное письмо под вакансию от LLM; null — расширение берёт свой шаблон
    public string? CoverLetter { get; private set; }

    public string? CoverLetterModel { get; private set; }

    public DateTimeOffset? CoverLetterAt { get; private set; }

    // Анкета работодателя с ответами. Видна только со входом на hh.ru — её присылает расширение
    public List<VacancyQuestion> Questions { get; private set; } = [];

    // Переходы между статусами, по порядку
    public List<ApplicationEvent> History { get; private set; } = [];

    // Скрыт из списка: меняется только показ, статус и история остаются (#71). Скрытым может быть только отсеянный отклик
    public DateTimeOffset? HiddenAt { get; private set; }

    public static JobApplication Create(Vacancy vacancy, Guid? userId, DateTimeOffset at, int? searchId = null) => new()
    {
        Vacancy = vacancy,
        VacancyId = vacancy.Id,
        UserId = userId,
        SearchId = searchId,
        CreatedAt = at,
        Status = VacancyStatus.New,
        History = [new ApplicationEvent { At = at, To = VacancyStatus.New }],
    };

    // Оценка правилами поиска. awaitLlm — прошедший правила отклик ждёт LLM-оценки (Pending), а не идёт сразу в очередь
    public void Evaluate(VacancyEvaluator evaluator, FilterRules rules, IEnumerable<ResumeProfile> resumes, DateTimeOffset at, bool awaitLlm = false)
    {
        if (Status is not (VacancyStatus.New or VacancyStatus.Queued or VacancyStatus.Skipped))
            throw new DomainException($"Отклик в статусе {Status} нельзя переоценить");

        var evaluation = evaluator.Evaluate(Vacancy, rules, resumes);
        (Score, ResumeProfile) = (evaluation.Score, evaluation.ResumeProfile);
        MoveTo(awaitLlm && evaluation.Status == VacancyStatus.Queued ? VacancyStatus.Pending : evaluation.Status, evaluation.Reason, at);
    }

    // Страница вакансии недоступна, подтвердить импортную карточку нечем: отклик больше не ждёт (#59).
    // Только из New: уже оценённые, отправленные и решённые отклики не трогаем
    public void MarkVacancyUnavailable(DateTimeOffset at)
    {
        if (Status != VacancyStatus.New)
            throw new DomainException($"Отклик в статусе {Status} не ждёт подтверждения вакансии");

        MoveTo(VacancyStatus.Skipped, VacancyUnavailable, at);
    }

    // LLM-оценка под резюме: подходящие — в очередь, сомнительные — на ручную проверку (Review; reviewDoubtful = false —
    // правило поиска «без проверки», сразу в очередь), неподходящие — пропуск с причиной.
    // resume — имя профиля, выбранное LLM; null — оставить выбор правил
    public void ApplyLlmEvaluation(LlmVerdict verdict, int score, string reason, string? resume, string model, DateTimeOffset at, bool reviewDoubtful)
    {
        if (Status != VacancyStatus.Pending)
            throw new DomainException($"LLM-оценка возможна только для отклика в статусе Pending, сейчас {Status}");

        (LlmVerdict, LlmScore, LlmReason, LlmModel, LlmEvaluatedAt) = (verdict, Math.Clamp(score, 0, 100), reason, model, at);
        Score = LlmScore.Value;
        if (!string.IsNullOrWhiteSpace(resume))
            ResumeProfile = resume;

        if (verdict == Vacancies.LlmVerdict.Reject)
        {
            MoveTo(VacancyStatus.Skipped, $"LLM: {reason}", at);
            return;
        }

        var doubtful = verdict == Vacancies.LlmVerdict.Doubtful;
        MoveTo(doubtful && reviewDoubtful ? VacancyStatus.Review : VacancyStatus.Queued, doubtful ? $"LLM сомневается: {reason}" : null, at);
    }

    // Ручная проверка: одобрить — в очередь; coverLetter — письмо, поправленное человеком (null — оставить письмо LLM);
    // answers — ответы анкеты, поправленные человеком, все по порядку вопросов (null — оставить ответы LLM)
    public void Approve(string? coverLetter, DateTimeOffset at, IReadOnlyList<IReadOnlyCollection<string>>? answers = null)
    {
        if (Status != VacancyStatus.Review)
            throw new DomainException($"Одобрить можно только отклик на проверке, сейчас {Status}");

        // Ответы — до письма: при ошибке в ответах письмо не меняется
        var answered = answers is null ? null : Answered(answers);
        if (!string.IsNullOrWhiteSpace(coverLetter))
            SetCoverLetter(coverLetter, ManualLetter, at);
        if (answered is not null)
            Questions = answered;
        MoveTo(VacancyStatus.Queued, "одобрено вручную", at);
    }

    // Ручная проверка: отклонить — пропуск с причиной
    public void Decline(string? reason, DateTimeOffset at)
    {
        if (Status != VacancyStatus.Review)
            throw new DomainException($"Отклонить можно только отклик на проверке, сейчас {Status}");

        MoveTo(VacancyStatus.Skipped, string.IsNullOrWhiteSpace(reason) ? "отклонено вручную" : $"отклонено вручную: {reason.Trim()}", at);
    }

    // Письмо готовится до отклика: для отклика в очереди или на проверке
    public void SetCoverLetter(string text, string model, DateTimeOffset at)
    {
        if (Status is not (VacancyStatus.Queued or VacancyStatus.Review))
            throw new DomainException($"Письмо готовится только для отклика в очереди или на проверке, сейчас {Status}");

        if (string.IsNullOrWhiteSpace(text))
            throw new DomainException("Пустое сопроводительное письмо");

        text = text.Trim();
        CoverLetter = text.Length > MaxCoverLetterLength ? text[..MaxCoverLetterLength] : text;
        (CoverLetterModel, CoverLetterAt) = (model, at);
    }

    // Сбросить письмо, чтобы LLM написала его заново (поменялись резюме, шаблон или промпт)
    public void ResetCoverLetter()
    {
        if (Sent)
            throw new DomainException("Отклик уже отправлен — письмо не переписать");

        (CoverLetter, CoverLetterModel, CoverLetterAt) = (null, null, null);
    }

    // Расширение встретило анкету: вопросы ждут ответов LLM, отклик уходит из очереди.
    // Анкета могла поменяться — прежние ответы сбрасываются.
    // Только из очереди (или повторно из анкеты): ответы LLM могут вести сразу в очередь, и анкета не должна вернуть туда
    // отклонённое вручную, ждущее проверки или ещё не оценённое
    public void RequireAnswers(IReadOnlyCollection<VacancyQuestion> questions, DateTimeOffset at)
    {
        if (Status is not (VacancyStatus.Queued or VacancyStatus.Questionnaire))
            throw new DomainException($"Анкета принимается только для отклика в очереди, сейчас {Status}");

        if (questions.Count == 0)
            throw new DomainException("В анкете нет вопросов");

        Questions = questions.ToList();
        MoveTo(VacancyStatus.Questionnaire, "анкета: ждёт ответов LLM", at);
    }

    // Ответы LLM в порядке вопросов. Неполные или с несуществующим вариантом — DomainException, отклик остаётся ждать.
    // Решение владельца (#58, пересмотр решения 2026-10-25): текстовые ответы LLM пишет сама и они уходят работодателю
    // от имени кандидата — на ручную проверку (Review), кроме поиска «без проверки» (reviewText = false); ответы только
    // вариантами — сразу в очередь. problems — цифры не из фактов после повторной попытки: на проверку всегда
    public void AnswerQuestions(IReadOnlyList<IReadOnlyCollection<string>> answers, DateTimeOffset at, bool reviewText,
        IReadOnlyCollection<string>? problems = null)
    {
        if (Status != VacancyStatus.Questionnaire)
            throw new DomainException($"Ответы на анкету возможны только в статусе Questionnaire, сейчас {Status}");

        Questions = Answered(answers);
        if (problems is { Count: > 0 })
            MoveTo(VacancyStatus.Review, $"анкета: проверить — {string.Join("; ", problems)}", at);
        else if (reviewText && Questions.Any(q => q.Type == QuestionType.Text))
            MoveTo(VacancyStatus.Review, "анкета: проверить ответы LLM", at);
        else
            MoveTo(VacancyStatus.Queued, "анкета: ответы LLM", at);
    }

    // LLM не ответила на все вопросы: решает человек. Одобрить — в очередь, расширение пришлёт анкету заново
    public void RequireQuestionnaireReview(string reason, DateTimeOffset at)
    {
        if (Status != VacancyStatus.Questionnaire)
            throw new DomainException($"Анкета уходит на проверку только из статуса Questionnaire, сейчас {Status}");

        MoveTo(VacancyStatus.Review, $"анкета: проверить — {reason}", at);
    }

    // Письмо LLM не прошло проверку: без письма отклик не уходит сам, решает человек
    public void RequireLetterReview(string reason, DateTimeOffset at)
    {
        if (Status is not (VacancyStatus.Queued or VacancyStatus.Review))
            throw new DomainException($"Письмо проверяется только для отклика в очереди или на проверке, сейчас {Status}");

        MoveTo(VacancyStatus.Review, $"{LetterReviewPrefix} — {reason}", at);
    }

    // На проверке из-за письма LLM, а не сомнительной оценки
    public bool LetterRejected => StatusReason?.StartsWith(LetterReviewPrefix, StringComparison.Ordinal) == true;

    // Новый список вопросов с ответами; сначала проверяются все, чтобы при ошибке не остались ответы наполовину
    private List<VacancyQuestion> Answered(IReadOnlyList<IReadOnlyCollection<string>> answers)
    {
        if (answers.Count != Questions.Count)
            throw new DomainException($"Ответов {answers.Count}, а вопросов {Questions.Count}");

        var answered = Questions.Select(q => VacancyQuestion.Create(q.Text, q.Type, q.Options)).ToList();
        for (var i = 0; i < answered.Count; i++)
            answered[i].SetAnswer(answers[i]);
        return answered;
    }

    // Отклик отправлен из очереди. Только из Queued и Questionnaire: не прошедший оценку или проверку отметить отправленным нельзя (#60)
    public void MarkApplied(string? resume, DateTimeOffset at)
    {
        if (Sent)
            throw new DomainException("Отклик на вакансию уже отправлен");

        if (Status is not (VacancyStatus.Queued or VacancyStatus.Questionnaire))
            throw new DomainException($"Отправленным можно отметить только отклик в очереди или с анкетой, сейчас {Status}");

        AppliedAt = at;
        if (!string.IsNullOrWhiteSpace(resume))
            ResumeProfile = resume;
        MoveTo(VacancyStatus.Applied, null, at);
    }

    // Чат на hh.ru есть, а статус потерялся: отклик уже у работодателя, поэтому из любого статуса до отправки (#26, #60)
    public void RecoverApplied(DateTimeOffset at)
    {
        if (Sent)
            throw new DomainException("Отклик на вакансию уже отправлен");

        AppliedAt = at;
        MoveTo(VacancyStatus.Applied, "восстановлен по чату hh.ru", at);
    }

    public void Skip(string? reason, DateTimeOffset at)
    {
        if (Sent)
            throw new DomainException("Нельзя пропустить вакансию, на которую уже откликнулись");

        MoveTo(VacancyStatus.Skipped, reason ?? StatusReason, at);
    }

    public void Fail(string? reason, DateTimeOffset at)
    {
        if (Sent)
            throw new DomainException("Отклик уже отправлен — ошибка отклика невозможна");

        MoveTo(VacancyStatus.Failed, reason ?? StatusReason, at);
    }

    // Работодатель отказал после отклика или после приглашения
    public void Reject(string? reason, DateTimeOffset at)
    {
        if (Status is not (VacancyStatus.Applied or VacancyStatus.Invited))
            throw new DomainException("Отказ возможен только после отклика");

        MoveTo(VacancyStatus.Rejected, reason ?? StatusReason, at);
    }

    // Работодатель пригласил (из чата hh.ru, #26)
    public void Invite(string? reason, DateTimeOffset at)
    {
        if (Status != VacancyStatus.Applied)
            throw new DomainException("Приглашение возможно только после отклика");

        MoveTo(VacancyStatus.Invited, reason, at);
    }

    // Отклик уже у работодателя: письмо, анкету и статус до отклика не поменять
    public bool Sent => Status is VacancyStatus.Applied or VacancyStatus.Invited or VacancyStatus.Rejected;

    // Вернуть в очередь пропущенный или упавший отклик
    public void Requeue(DateTimeOffset at)
    {
        if (Status is not (VacancyStatus.Skipped or VacancyStatus.Failed))
            throw new DomainException($"Из статуса {Status} в очередь вернуть нельзя");

        MoveTo(VacancyStatus.Queued, null, at);
    }

    // Отсеян до отклика — человеком, LLM или правилами: такой отклик можно оценить заново и скрыть (#71).
    // Failed не входит: отклик «не подтверждён» мог уйти на hh.ru, для него есть «В очередь»
    public bool Dismissed => Status == VacancyStatus.Skipped;

    // Оценить заново: письмо LLM и ответы анкеты привязаны к прежнему резюме и сбрасываются, письмо человека остаётся.
    // Импортная карточка ждёт в New, её подтвердит и оценит сбор; остальные — сразу правилами и затем в Pending на LLM.
    // Без настроенной LLM сценарий не запускается: это проверяет обработчик
    public void Reevaluate(VacancyEvaluator evaluator, FilterRules rules, IEnumerable<ResumeProfile> resumes, DateTimeOffset at)
    {
        if (!Dismissed)
            throw new DomainException($"Из статуса {Status} оценить заново нельзя");

        if (CoverLetterModel != ManualLetter)
            ResetCoverLetter();
        Questions = [];
        (LlmVerdict, LlmScore, LlmReason, LlmModel, LlmEvaluatedAt) = (null, null, null, null, null);

        // Через New: правила могут снова отсеять, и без этого шага в истории не осталось бы записи
        MoveTo(VacancyStatus.New, Reevaluation, at);
        if (!Vacancy.Imported)
            Evaluate(evaluator, rules, resumes, at, awaitLlm: true);
    }

    // Повторное скрытие оставляет дату первого
    public void Hide(DateTimeOffset at)
    {
        if (!Dismissed)
            throw new DomainException($"Скрыть можно только отсеянный отклик, сейчас {Status}");

        HiddenAt ??= at;
    }

    public void Unhide() => HiddenAt = null;

    // Смена статуса с записью в историю; без смены статуса меняется только причина. Новый статус снимает скрытие:
    // иначе отклик в очереди или отправленный остался бы скрыт.
    // Список пересобирается, а не дополняется: так EF видит изменение JSON-колонки
    private void MoveTo(VacancyStatus status, string? reason, DateTimeOffset at)
    {
        if (status != Status)
        {
            History = [.. History, new ApplicationEvent { At = at, From = Status, To = status, Reason = reason }];
            HiddenAt = null;
        }

        (Status, StatusReason) = (status, reason);
    }

    // Правила поиска для оценки и анкеты; поиск удалён или не загружен — правила по умолчанию
    public FilterRules Rules => Search?.Rules ?? new FilterRules();

    // Модель письма, поправленного человеком на ручной проверке
    public const string ManualLetter = "manual";

    // Причина пропуска, когда страница вакансии не открылась при подтверждении импортной карточки
    public const string VacancyUnavailable = "вакансия недоступна";

    // Причина в истории, когда человек вернул отклик на оценку
    public const string Reevaluation = "повторная оценка";

    private const string LetterReviewPrefix = "письмо: проверить";

    // С запасом под лимит поля письма на hh.ru
    private const int MaxCoverLetterLength = 4000;
}