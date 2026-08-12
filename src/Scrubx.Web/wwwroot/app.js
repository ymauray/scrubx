const rulesListEl = document.getElementById("rules-list");
const formEl = document.getElementById("upload-form");
const fileInputEl = document.getElementById("file-input");
const submitBtnEl = document.getElementById("submit-btn");
const reportEl = document.getElementById("report");

let rules = [];

const DISABLED_RULES_STORAGE_KEY = "scrubx.disabledRules";

function loadDisabledRuleNames() {
  try {
    const raw = localStorage.getItem(DISABLED_RULES_STORAGE_KEY);
    return raw ? new Set(JSON.parse(raw)) : new Set();
  } catch {
    return new Set();
  }
}

function saveDisabledRuleNames() {
  try {
    localStorage.setItem(DISABLED_RULES_STORAGE_KEY, JSON.stringify(getDisabledRuleNames()));
  } catch {
    // localStorage indisponible (navigation privée, quota, etc.) : la préférence ne sera simplement pas retenue.
  }
}

async function loadRules() {
  const res = await fetch("/api/rules");
  rules = await res.json();
  const disabledRuleNames = loadDisabledRuleNames();

  const themes = new Map();
  for (const rule of rules) {
    if (!themes.has(rule.theme)) themes.set(rule.theme, []);
    themes.get(rule.theme).push(rule);
  }

  rulesListEl.innerHTML = "";
  for (const [theme, themeRules] of themes) {
    const heading = document.createElement("h3");
    heading.className = "rules-theme";
    heading.textContent = theme;
    rulesListEl.appendChild(heading);

    for (const rule of themeRules) {
      const label = document.createElement("label");
      label.className = "rule-item";

      const checkbox = document.createElement("input");
      checkbox.type = "checkbox";
      checkbox.checked = !disabledRuleNames.has(rule.ruleName);
      checkbox.value = rule.ruleName;

      const span = document.createElement("span");
      span.textContent = rule.title;
      if (rule.isWarningByDefault) {
        const badge = document.createElement("span");
        badge.className = "badge";
        badge.textContent = "avertissement";
        span.appendChild(badge);
      }

      label.appendChild(checkbox);
      label.appendChild(span);
      rulesListEl.appendChild(label);
    }
  }
}

rulesListEl.addEventListener("change", (event) => {
  if (event.target.matches("input[type=checkbox]")) {
    saveDisabledRuleNames();
  }
});

function getDisabledRuleNames() {
  return [...rulesListEl.querySelectorAll("input[type=checkbox]")]
    .filter(cb => !cb.checked)
    .map(cb => cb.value);
}

function renderReport(data) {
  reportEl.innerHTML = "";

  const errors = data.errors.filter(e => !e.isWarning);
  const warnings = data.errors.filter(e => e.isWarning);

  const summary = document.createElement("div");
  if (errors.length > 0) {
    summary.className = "report-summary error";
    summary.textContent = "Des erreurs de validation ont été détectées.";
  } else if (warnings.length > 0) {
    summary.className = "report-summary warning";
    summary.textContent = "Le document est valide, mais des avertissements ont été relevés.";
  } else {
    summary.className = "report-summary ok";
    summary.textContent = "Félicitations ! Le document est parfaitement valide.";
  }
  reportEl.appendChild(summary);

  renderGroup(errors, "error");
  renderGroup(warnings, "warning");
}

function renderGroup(issues, kind) {
  if (issues.length === 0) return;

  const groups = new Map();
  for (const issue of issues) {
    if (!groups.has(issue.ruleName)) groups.set(issue.ruleName, { title: issue.title, items: [] });
    groups.get(issue.ruleName).items.push(issue);
  }

  for (const [, group] of groups) {
    const wrapper = document.createElement("div");
    wrapper.className = "issue-group";

    const titleEl = document.createElement("div");
    titleEl.className = `title ${kind}`;
    titleEl.textContent = `${group.title} `;
    const countEl = document.createElement("span");
    countEl.className = "count";
    countEl.textContent = `(${group.items.length} occurrence${group.items.length > 1 ? "s" : ""})`;
    titleEl.appendChild(countEl);
    wrapper.appendChild(titleEl);

    for (const item of group.items) {
      if (item.context) {
        const ctxEl = document.createElement("div");
        ctxEl.className = "issue-context";
        ctxEl.textContent = item.context;
        wrapper.appendChild(ctxEl);
      }
    }

    reportEl.appendChild(wrapper);
  }
}

formEl.addEventListener("submit", async (event) => {
  event.preventDefault();

  const file = fileInputEl.files[0];
  if (!file) return;

  submitBtnEl.disabled = true;
  reportEl.textContent = "Analyse en cours...";

  try {
    const formData = new FormData();
    formData.append("file", file);
    formData.append("disabledRules", getDisabledRuleNames().join(","));

    const res = await fetch("/api/validate", { method: "POST", body: formData });
    const data = await res.json();

    if (!res.ok) {
      reportEl.textContent = `Erreur : ${data.error ?? "requête invalide."}`;
      return;
    }

    renderReport(data);
  } catch (err) {
    reportEl.textContent = `Erreur inattendue : ${err.message}`;
  } finally {
    submitBtnEl.disabled = false;
  }
});

loadRules();
