// node --test scripts/check-public-privacy.test.mjs
import assert from "node:assert/strict";
import { readFileSync } from "node:fs";
import { test } from "node:test";

import { scanText, sha256 } from "./check-public-privacy.mjs";

const kinds = (text, hashes = []) => scanText(text, hashes).map((hit) => hit.kind);

test("private IPv4 addresses are found in all three private ranges", () => {
  assert.deepEqual(kinds("open http://10.4.5.6:9347/ now"), ["privateIp"]);
  assert.deepEqual(kinds("host 172.16.0.9 and 172.31.255.1"), ["privateIp", "privateIp"]);
  assert.deepEqual(kinds("at 192.168.0.12"), ["privateIp"]);
});

test("documentation addresses, public addresses and other ranges are fine", () => {
  assert.deepEqual(kinds("192.0.2.10, 198.51.100.7 and 203.0.113.250"), []);
  assert.deepEqual(kinds("172.15.0.1 and 172.32.0.1 are not private; 8.8.8.8 is public"), []);
  assert.deepEqual(kinds("127.0.0.1 and 0.0.0.0"), []);
});

test("a version number or a longer dotted number is not an address", () => {
  assert.deepEqual(kinds("version 1.10.4.5 and build 10.0.26300.1234"), []);
});

test("a private range written as CIDR with a zero host is allowed, a machine inside one is not", () => {
  assert.deepEqual(kinds("trust 10.0.0.0/8, 172.16.0.0/12 and 192.168.0.0/16"), []);
  assert.deepEqual(kinds("trust 10.0.0.5/24"), ["privateIp"]);
});

test("Windows default machine names are found", () => {
  assert.deepEqual(kinds("on WIN-AB12CD34EF56 today"), ["machineName"]);
  assert.deepEqual(kinds("WIN-SHORT and Windows-Server are fine"), []);
});

test("UNC paths are found, placeholders are not", () => {
  assert.deepEqual(kinds(String.raw`share \\storage\media\tv`), ["uncPath"]);
  assert.deepEqual(kinds(String.raw`share "\\\\storage\\media"`), ["uncPath"]);
  assert.deepEqual(kinds(String.raw`share \\<nas>\<share>\tv and \\<host>\media`), []);
});

test("an escaped drive path or a unicode escape is not a UNC path", () => {
  assert.deepEqual(kinds(String.raw`"C:\\Data\\Media" and "caf\\u00e9\\"`), []);
});

test("email addresses are found, noreply and example ones are not", () => {
  assert.deepEqual(kinds("write to jane.doe@mail.test"), ["email"]);
  assert.deepEqual(kinds("noreply@anthropic.com, 123+jane@users.noreply.github.com, me@example.com, a@sub.example.org"), []);
});

test("api key values are found, placeholders are not", () => {
  assert.deepEqual(kinds("?api_key=a1b2c3d4e5f6a7b8c9d0e1f2a3b4c5d6"), ["apiKey"]);
  assert.deepEqual(kinds('{"apikey": "9f8e7d6c5b4a39281716"}'), ["apiKey"]);
  assert.deepEqual(kinds("api_key=<your-key>, apikey=YOUR_API_KEY_HERE, api_key=xxxxxxxxxxxxxxxx, api_key=changeme-changeme"), []);
  assert.deepEqual(kinds("the X-Api-Key header and api_key=header_name_only"), []);
});

test("a known private word is found by its hash, in any case and inside a longer name", () => {
  const hashes = [sha256("lab-box"), sha256("10.9.9.")];
  assert.deepEqual(kinds("connect to LAB-BOX now", hashes), ["knownLiteral"]);
  assert.deepEqual(kinds("name: lab-box.local", hashes), ["knownLiteral"]);
  assert.deepEqual(kinds("my-lab-box-2", hashes), ["knownLiteral"]);
  assert.deepEqual(kinds("reach 10.9.9.40", hashes).sort(), ["knownLiteral", "privateIp"]);
  assert.deepEqual(kinds("a laboratory box and 10.9.8.1", hashes), ["privateIp"]);
});

test("a hit names its line and never the matched text", () => {
  const hits = scanText("fine\nfine\nat 192.168.7.7\n", []);
  assert.deepEqual(hits, [{ kind: "privateIp", line: 3 }]);
});

test("the shipped hash list holds exactly two SHA-256 hashes and no readable words", () => {
  const { sha256: list } = JSON.parse(readFileSync(new URL("./public-privacy-hashes.json", import.meta.url), "utf8"));
  assert.equal(list.length, 2);
  for (const entry of list) assert.match(entry, /^[0-9a-f]{64}$/);
});
