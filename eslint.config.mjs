import { defineConfig } from "eslint/config";
import globals from "globals";

export default defineConfig([
  {
    files: ["server/src/Lucky5.Api/wwwroot/js/**/*.js"],
    ignores: ["server/src/Lucky5.Api/wwwroot/js/signalr.min.js"],
    languageOptions: {
      ecmaVersion: 2022,
      sourceType: "script",
      globals: {
        ...globals.browser,
        CabinetClock: "readonly",
        CabinetStage: "readonly",
        CabinetOrchestrator: "readonly",
        CabinetAudio: "readonly",
        CabinetShell: "readonly",
        CabinetState: "readonly",
        CabinetPace: "readonly",
        CabinetTransition: "readonly",
        CabinetBonus: "readonly",
        CabinetFirebase: "readonly",
        CabinetImageCache: "readonly",
        CabinetAI9Buttons: "readonly",
        CabinetV8Effects: "readonly",
        CabinetInput: "readonly",
        PaytableCanvas: "readonly",
        DuBoardCanvas: "readonly",
        Wakelock: "readonly",
        GAME_CONFIG: "readonly",
        LUCKY5_API_BASE_URL: "readonly",
        LUCKY5_FIREBASE_CONFIG: "readonly",
      },
    },
    rules: {
      "no-unused-vars": "warn",
      "no-undef": "warn",
      "no-extra-semi": "warn",
      "no-constant-condition": "warn",
      "no-cond-assign": "warn",
      "no-debugger": "warn",
    },
  },
]);