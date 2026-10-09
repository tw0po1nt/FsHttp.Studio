import { after, afterEach, before, beforeEach } from "vscode-extension-tester";

/**
 * Wire Mocha hooks through ExTester so hook failures capture screenshots.
 *
 * @param {() => Promise<void>} harnessSetupFn
 * @param {() => Promise<void>} beforeEachFn
 * @param {(test: import('mocha').Test | undefined) => Promise<void>} afterEachFn
 * @param {() => Promise<void>} afterFn
 */
export function registerHarnessHooks(harnessSetupFn, beforeEachFn, afterEachFn, afterFn) {
  before("Harness setup", function () {
    this.timeout(300000);
    return harnessSetupFn();
  });

  beforeEach(function () {
    return beforeEachFn();
  });

  afterEach(function () {
    return afterEachFn(this.currentTest);
  });

  after(function () {
    return afterFn();
  });
}
