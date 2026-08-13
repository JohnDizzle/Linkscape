const version = new URLSearchParams(window.location.search).get("version");

const normalizeVersion = (value) => {
  const match = value?.trim().match(/^(\d+)\.(\d+)\.(\d+)/);
  return match ? `${match[1]}.${match[2]}.${match[3]}` : "";
};

if (version) {
  document.getElementById("package-version").textContent = version;
}

const releaseVersion = normalizeVersion(version);
const currentRelease = [...document.querySelectorAll(".timeline-release")]
  .find((release) => release.dataset.version === releaseVersion);

if (currentRelease) {
  currentRelease.classList.add("current-release", "is-focusing");

  const versionBadge = currentRelease.querySelector(".release-version");
  if (versionBadge) {
    versionBadge.textContent = `${versionBadge.textContent} - Installed`;
  }

  const releaseLabel = currentRelease.querySelector("[data-release-label]");
  if (releaseLabel) {
    releaseLabel.textContent = "INSTALLED RELEASE";
  }

  const releaseNote = document.getElementById("current-release-note");
  if (releaseNote) {
    releaseNote.textContent = `Version ${version} is highlighted below. Travel down the visual changelog to see how LinkScape's features progressed.`;
  }

  requestAnimationFrame(() => {
    currentRelease.scrollIntoView({ behavior: "smooth", block: "start" });
  });

  currentRelease.addEventListener("animationend", () => {
    currentRelease.classList.remove("is-focusing");
  }, { once: true });
}

for (const video of document.querySelectorAll(".video-stage video")) {
  const stage = video.closest(".video-stage");
  let previewRequested = false;

  const revealPreview = () => {
    if (!stage || video.readyState < HTMLMediaElement.HAVE_CURRENT_DATA) {
      return;
    }

    if (!previewRequested && video.duration > 0.15 && video.currentTime < 0.05) {
      previewRequested = true;
      video.currentTime = 0.1;
      return;
    }

    stage.classList.add("is-ready");
  };

  video.addEventListener("loadeddata", revealPreview, { once: true });
  video.addEventListener("seeked", revealPreview, { once: true });
  video.addEventListener("error", () => {
    const status = stage?.querySelector(".video-loading span");
    if (status) {
      status.textContent = "Preview unavailable - use the video controls to retry.";
    }
  }, { once: true });

  video.load();
}
