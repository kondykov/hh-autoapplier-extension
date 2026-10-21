export const PANEL_CSS = `
#hh-autoapply-panel{position:fixed;bottom:20px;right:20px;width:400px;max-height:520px;background:#1a1a2e;border:2px solid #e94560;border-radius:12px;z-index:2147483647;font:13px -apple-system,BlinkMacSystemFont,'Segoe UI',Roboto,sans-serif;color:#eee;overflow:hidden;box-shadow:0 8px 32px rgba(0,0,0,.4);display:flex;flex-direction:column}
#hh-autoapply-panel.minimized{max-height:46px}
#hh-autoapply-header{background:#e94560;padding:12px 16px;cursor:move;display:flex;justify-content:space-between;align-items:center;font-weight:bold;font-size:14px;user-select:none}
#hh-autoapply-header button{background:none;border:none;color:#fff;font-size:18px;cursor:pointer;padding:0 4px}
#hh-autoapply-body{padding:12px 16px;overflow-y:auto;flex:1}
.hh-step-info{background:#16213e;border-radius:8px;padding:10px 12px;margin:8px 0;border-left:3px solid #e94560}
#hh-captcha-banner{background:#e94560;color:#fff;font-weight:bold;padding:8px;border-radius:6px;margin-bottom:8px;text-align:center;animation:hh-blink 1s infinite}
#hh-captcha-banner[hidden]{display:none}
@keyframes hh-blink{50%{opacity:.5}}
.hh-step-label{color:#e94560;font-weight:bold;font-size:12px;text-transform:uppercase}
.hh-step-desc{margin-top:4px;color:#ccc;line-height:1.4;white-space:pre-line}
.hh-btn{padding:8px 16px;border:none;border-radius:6px;cursor:pointer;font-size:13px;font-weight:600;color:#fff;background:#555}
.hh-btn:hover{opacity:.9}
.hh-btn:disabled{opacity:.4;cursor:not-allowed}
.hh-btn-primary{background:#e94560}.hh-btn-secondary{background:#0f3460}.hh-btn-success{background:#4CAF50}.hh-btn-warn{background:#d29922}
.hh-btn-row{display:flex;gap:8px;margin:8px 0;flex-wrap:wrap}
.hh-stats{display:flex;gap:8px;flex-wrap:wrap}
.hh-stat{background:#16213e;padding:6px 12px;border-radius:6px;text-align:center}
.hh-stat-val{font-size:18px;font-weight:bold;color:#e94560}
.hh-stat-label{font-size:10px;color:#888;text-transform:uppercase}
.hh-input-group{margin:8px 0;display:flex;flex-direction:column;gap:4px}
.hh-input-group label{font-size:11px;color:#888;text-transform:uppercase;font-weight:bold}
.hh-input{background:#0d1117;border:1px solid #444;color:#eee;padding:6px 8px;border-radius:4px;font-size:12px;width:100%;box-sizing:border-box}
#hh-autoapply-log{background:#0d1117;border-radius:6px;padding:8px;max-height:120px;overflow-y:auto;font:11px Consolas,Monaco,monospace;margin-top:8px;line-height:1.5}
.log-info{color:#58a6ff}.log-success{color:#3fb950}.log-warn{color:#d29922}.log-error{color:#f85149}.log-step{color:#bc8cff}
#hh-autoapply-modal{position:fixed;inset:0;background:rgba(0,0,0,.6);z-index:2147483647;display:flex;align-items:center;justify-content:center}
#hh-autoapply-modal .box{background:#1a1a2e;color:#eee;border:2px solid #e94560;border-radius:12px;width:600px;max-height:80vh;overflow:auto;padding:16px;font:13px sans-serif}
#hh-autoapply-modal a{color:#58a6ff}
#hh-autoapply-modal li{margin:6px 0}
`;
