window.exporter = window.exporter || {};
window.exporter.download = function (filename, content) {
  var blob = new Blob([content], { type: "text/csv;charset=utf-8;" });
  var url = window.URL.createObjectURL(blob);
  var link = document.createElement("a");
  link.href = url;
  link.download = filename || "export.csv";
  document.body.appendChild(link);
  link.click();
  document.body.removeChild(link);
  window.URL.revokeObjectURL(url);
};

window.exporter.downloadBinary = function (filename, content, mimeType) {
  var bytes = content instanceof Uint8Array ? content : new Uint8Array(content);
  var blob = new Blob([bytes], { type: mimeType || "application/octet-stream" });
  var url = window.URL.createObjectURL(blob);
  var link = document.createElement("a");
  link.href = url;
  link.download = filename || "export.bin";
  document.body.appendChild(link);
  link.click();
  document.body.removeChild(link);
  window.URL.revokeObjectURL(url);
};
