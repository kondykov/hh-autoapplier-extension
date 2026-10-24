// Единственное место, где живут селекторы hh.ru
export const SELECTORS = {
  vacancyCard: '[data-qa="vacancy-serp__vacancy"]',
  vacancyTitle: '[data-qa="serp-item__title"]',
  companyName: '[data-qa="vacancy-serp__vacancy-employer"]',
  vacancyTitleLink: 'a[data-qa="serp-item__title"]',
  applyButton: '[data-qa="vacancy-serp__vacancy_response"]',
  respondedMarker: '[data-qa="vacancy-serp__vacancy_responded"]',

  responseModal: '[data-qa="vacancy-response-popup"]',
  responseModalTitle: '[data-qa="title"]',
  responseModalClose: '[data-qa="response-popup-close"]',
  coverLetterInput: '[data-qa="vacancy-response-popup-form-letter-input"]',
  addCoverLetterBtn: '[data-qa="add-cover-letter"]',
  coverLetterInputAlt:
    'form[id^="cover-letter-"] textarea[name="text"], [data-qa="textarea-native-wrapper"] textarea, textarea[name="text"]',
  submitButton:
    '[data-qa="vacancy-response-submit-popup"], [data-qa="vacancy-response-letter-submit"]',
  generateCoverLetterBtn: '[data-qa="generate-cover-letter"]',
  coverLetterToggle:
    '[data-qa="vacancy-response-letter-toggle"], [data-qa="vacancy-response-letter-toggle-text"]',

  relocationConfirmBtn: '[data-qa="relocation-warning-confirm"]',

  employerAskingForTest: '[data-qa="employer-asking-for-test"]',
  taskQuestion: '[data-qa="task-question"]',
  testDescription: '[data-qa="test-description"]',
  taskBody: '[data-qa="task-body"]',
  radioInput: 'input[type="radio"]',
  vacancyResponseForm: "#RESPONSE_MODAL_FORM_ID",

  alreadyRespondedText: '[data-qa="already-responded-text"]',
  resumeTitle: '[data-qa="resume-title"]',
  // Список резюме на форме отклика: заголовок внутри кнопки, варианты — элементы listbox (проверено вживую 2026-10-24)
  resumeTrigger: '[role="button"]',
  resumeOption: '[role="option"]',
  // Поле и картинка капчи hh.ru (/account/captcha, проверено 2026-10-24)
  captcha: '[data-qa="account-captcha-input"], [data-qa="account-captcha-picture"]',
  responseRejectWarning: '[data-qa="response-reject-warning"]',
  titleDescription: '[data-qa="title-description"]',

  responseCredsTitle: '[data-qa="vacancy-credentials"] [data-qa="cell-text-content"]',
  snippets: '[data-qa^="vacancy-serp__vacancy_snippet"]',
  cardFormatLabels: '[data-qa^="vacancy-label-work-schedule"], [data-qa*="work-format"]',
  pageSalary: '[data-qa="vacancy-salary"]',
  pageWorkFormat: '[data-qa="work-formats-text"], [data-qa="common-employment-text"]',
  pageSkills: '[data-qa="skills-element"]',
  pageDescription: '[data-qa="vacancy-description"]',
  responseMainTitle: 'h1[data-qa="title"]',
  responseCompanyAvatar: '[aria-label][class*="magritte-avatar"]',
  taskTextarea: "textarea",
  chatUnreadFilter: '[data-qa="chatik-filter-unread"], [data-qa*="unread"]',

  vacancyApplyTop: '[data-qa="vacancy-response-link-top"]',
  vacancyApplyBottom: '[data-qa="vacancy-response-link-bottom"]',
  anyResponseButton: 'button[data-qa*="response"]',

  chatUnreadBadge: '[data-qa="chatik-info-badges"]',
} as const;

export const TEXT_MARKERS = {
  coverLetterRequired: "Сопроводительное письмо обязательное",
  alreadyResponded: "Вы откликнулись",
} as const;
