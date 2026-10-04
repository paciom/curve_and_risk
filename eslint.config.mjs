import js from "@eslint/js";
import globals from "globals";

export default [
  { ignores: ["**/bin/**", "**/obj/**", "node_modules/**", "TestResults/**", "StrykerOutput/**", "benchmarks/**"] },
  js.configs.recommended,
  {
    files: ["**/*.mjs"],
    languageOptions: { ecmaVersion: 2024, sourceType: "module", globals: globals.node },
    rules: {
      "no-unused-vars": ["error", { argsIgnorePattern: "^_" }],
      "no-var": "error",
      "prefer-const": "error",
      eqeqeq: ["error", "always"],
      "no-shadow": "error",
      "no-implicit-globals": "error",
      complexity: ["error", 12],
      "max-depth": ["error", 3],
      "max-params": ["error", 4],
    },
  },
];
