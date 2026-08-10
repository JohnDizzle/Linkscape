const version = new URLSearchParams(window.location.search).get("version");

if (version) {
  document.getElementById("package-version").textContent = version;
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
      status.textContent = "Preview unavailable — use the video controls to retry.";
    }
  }, { once: true });

  video.load();
}
