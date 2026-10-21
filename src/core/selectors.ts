// Единственное место, где живут селекторы hh.ru (перенесены из legacy/hh-autoapply.user.js)
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
  responseRejectWarning: '[data-qa="response-reject-warning"]',
  titleDescription: '[data-qa="title-description"]',

  responseCredsTitle: '[data-qa="vacancy-credentials"] [data-qa="cell-text-content"]',
  responseMainTitle: 'h1[data-qa="title"]',

  vacancyApplyTop: '[data-qa="vacancy-response-link-top"]',
  vacancyApplyBottom: '[data-qa="vacancy-response-link-bottom"]',
  anyResponseButton: 'button[data-qa*="response"]',

  chatUnreadBadge: '[data-qa="chatik-info-badges"]',
} as const;

export const TEXT_MARKERS = {
  coverLetterRequired: "Сопроводительное письмо обязательное",
  alreadyResponded: "Вы откликнулись",
} as const;
