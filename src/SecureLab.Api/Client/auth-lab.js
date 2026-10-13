// Браузерний сценарій ЛР 03: register -> login -> profile -> create -> logout.
// Cookie сеансу додає сам браузер (credentials: same-origin). JavaScript її не читає
// і нічого не зберігає в localStorage, sessionStorage, dataset чи Console.

const sessionState = document.querySelector("#session-state");
const result = document.querySelector("#result");
const registerResult = document.querySelector("#register-result");
const createResult = document.querySelector("#create-result");
const profile = document.querySelector("#profile");

const profileFields = {
  id: document.querySelector("#profile-id"),
  userName: document.querySelector("#profile-username"),
  displayName: document.querySelector("#profile-displayname"),
  roles: document.querySelector("#profile-roles")
};

// Запити йдуть лише на origin цього застосунку; зовнішню адресу відхиляємо.
function apiFetch(path, options = {}) {
  const url = new URL(path, location.origin);
  if (url.origin !== location.origin) {
    throw new Error("Потрібна адреса цього застосунку.");
  }
  return fetch(url, { ...options, credentials: "same-origin" });
}

function postJson(path, body) {
  return apiFetch(path, {
    method: "POST",
    headers: { "Content-Type": "application/json" },
    body: JSON.stringify(body)
  });
}

function showState(text) {
  sessionState.textContent = text;
}

function clearProfile() {
  profile.hidden = true;
  for (const field of Object.values(profileFields)) {
    field.textContent = "";
  }
}

// Показуємо лише дозволені поля DTO через textContent; інші властивості відповіді не читаємо.
function renderProfile(data) {
  profileFields.id.textContent = String(data.id ?? "");
  profileFields.userName.textContent = String(data.userName ?? "");
  profileFields.displayName.textContent = String(data.displayName ?? "");
  profileFields.roles.textContent = Array.isArray(data.roles) ? data.roles.join(", ") : "";
  profile.hidden = false;
}

// Перевірка стану сеансу: 204 означає вхід, 401 анонімний стан, інше невизначений стан.
async function refreshSessionState() {
  try {
    const response = await apiFetch("/api/auth/session-check");
    if (response.status === 204) {
      showState("сеанс активний");
    } else if (response.status === 401) {
      showState("не ввійшли");
      clearProfile();
    } else {
      showState(`невідомо (${response.status})`);
    }
  } catch {
    showState("невідомо (помилка мережі)");
  }
}

async function describeFailure(response) {
  const contentType = response.headers.get("content-type") ?? "";
  if (!contentType.includes("json")) {
    return `${response.status}`;
  }
  const problem = await response.json();
  const fields = problem.errors ? Object.keys(problem.errors).join(", ") : "";
  return fields ? `${response.status}: перевірте поля ${fields}` : `${response.status}: ${problem.title ?? ""}`;
}

document.querySelector("#register").addEventListener("submit", async event => {
  event.preventDefault();
  const form = event.currentTarget;
  const data = Object.fromEntries(new FormData(form));
  // Пароль живе лише у формі та тілі запиту; поле очищаємо одразу.
  form.elements.password.value = "";
  try {
    const response = await postJson("/api/auth/register", data);
    if (response.status === 201) {
      const created = await response.json();
      registerResult.textContent = `Register: 201. Створено користувача ${created.userName}. Тепер увійдіть.`;
      form.reset();
    } else {
      registerResult.textContent = `Register: ${await describeFailure(response)}`;
    }
  } catch {
    registerResult.textContent = "Помилка мережі під час реєстрації.";
  } finally {
    delete data.password;
  }
});

document.querySelector("#login").addEventListener("submit", async event => {
  event.preventDefault();
  const form = event.currentTarget;
  const data = Object.fromEntries(new FormData(form));
  form.elements.password.value = "";
  try {
    const response = await postJson("/api/auth/login", data);
    result.textContent = `Login: ${response.status}`;
    if (response.status === 204) {
      await loadProfile();
    }
  } catch {
    result.textContent = "Помилка мережі під час входу.";
  } finally {
    delete data.password;
    await refreshSessionState();
  }
});

async function loadProfile() {
  try {
    const response = await apiFetch("/api/me");
    const contentType = response.headers.get("content-type") ?? "";
    if (response.ok && contentType.includes("application/json")) {
      renderProfile(await response.json());
      result.textContent = "Профіль: 200";
    } else if (response.status === 401) {
      clearProfile();
      result.textContent = "Профіль: 401 (потрібен вхід)";
    } else {
      clearProfile();
      result.textContent = `Профіль: ${response.status}`;
    }
  } catch {
    clearProfile();
    result.textContent = "Помилка мережі під час читання профілю.";
  }
}

document.querySelector("#me").addEventListener("click", loadProfile);

document.querySelector("#logout").addEventListener("click", async () => {
  try {
    const response = await apiFetch("/api/auth/logout", { method: "POST" });
    result.textContent = `Logout: ${response.status}`;
  } catch {
    result.textContent = "Помилка мережі під час виходу.";
  } finally {
    // Очищення UI не замінює sign-out: стан і профіль перевіряємо повторним запитом.
    clearProfile();
    createResult.textContent = "";
    await refreshSessionState();
  }
});

document.querySelector("#create").addEventListener("submit", async event => {
  event.preventDefault();
  const form = event.currentTarget;
  const data = Object.fromEntries(new FormData(form));
  // Request DTO лише з полів контракту ЛР 2. Власника, статус, Id призначає сервер.
  const body = {
    title: data.title,
    description: data.description,
    severity: data.severity,
    occurredAtUtc: new Date(data.occurredAtUtc + "Z").toISOString()
  };
  try {
    const response = await postJson("/api/incidents", body);
    if (response.status === 201) {
      const created = await response.json();
      createResult.textContent = `Create: 201, інцидент ${created.id} (${created.status})`;
      form.reset();
    } else if (response.status === 401) {
      createResult.textContent = "Create: 401 (потрібен вхід)";
    } else {
      createResult.textContent = `Create: ${await describeFailure(response)}`;
    }
  } catch {
    createResult.textContent = "Помилка мережі під час створення.";
  }
});

clearProfile();
refreshSessionState();
