(function () {
  let chart = null;
  const isKorean = () => document.documentElement.lang === "ko";
  window.addEventListener("ops-language-changed", () => {
    if (!chart) return;
    chart.data.datasets[0].label = isKorean() ? "요청 수" : "Requests";
    chart.update("none");
  });

  function getThemeColors() {
    const styles = getComputedStyle(document.documentElement);
    return {
      text: styles.getPropertyValue("--text-secondary").trim() || "#64748b",
      grid: styles.getPropertyValue("--card-border").trim() || "#dbe3ef",
      accent: styles.getPropertyValue("--accent").trim() || "#2563eb"
    };
  }

  window.logParserAnalysis = {
    initTopIpChart: function (canvasId, items) {
      const canvas = document.getElementById(canvasId);
      if (!canvas || typeof Chart === "undefined") return;

      if (chart) chart.destroy();
      const colors = getThemeColors();
      const rows = Array.isArray(items) ? items : [];
      chart = new Chart(canvas, {
        type: "bar",
        data: {
          labels: rows.map(x => x.value ?? x.Value ?? "-"),
          datasets: [{
            label: isKorean() ? "요청 수" : "Requests",
            data: rows.map(x => x.count ?? x.Count ?? 0),
            backgroundColor: "color-mix(in srgb, " + colors.accent + " 78%, transparent)",
            borderColor: colors.accent,
            borderWidth: 1,
            borderRadius: 8,
            barThickness: 22
          }]
        },
        options: {
          indexAxis: "y",
          responsive: true,
          maintainAspectRatio: false,
          plugins: {
            legend: { display: false },
            tooltip: {
              callbacks: {
                label: context => isKorean()
                  ? ` ${Number(context.raw || 0).toLocaleString("ko-KR")}건`
                  : ` ${Number(context.raw || 0).toLocaleString("en-US")} requests`
              }
            }
          },
          scales: {
            x: {
              beginAtZero: true,
              ticks: { color: colors.text, precision: 0 },
              grid: { color: colors.grid }
            },
            y: {
              ticks: { color: colors.text },
              grid: { display: false }
            }
          }
        }
      });
    }
  };
})();
