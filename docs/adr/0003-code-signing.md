# Sign releases with Azure Artifact Signing, not an OV certificate

Releases are signed as junis GmbH with Azure Artifact Signing (formerly Trusted Signing), Basic tier,
about €105 a year. CI signs through vpk's `--azureTrustedSignFile` and reaches Azure over GitHub OIDC
as a dedicated managed identity, never as the junis-hosted App Registration. The project is run on a
voluntary basis, and cost decided between the options that work. Prices and rules below are as
researched on 2026-10-17.

## Considered options

- **OV certificate, key in junis' own Azure Key Vault Premium** (SSL.com). About $690 in year 1:
  $129 certificate, $500 Key Vault confirmation, the HSM key. Its one advantage is a leaf
  certificate that lives up to 460 days, which a customer can pin as a Defender certificate Allow
  indicator (honoured by both ASR rules seen in the field) or as an AllSigned TrustedPublisher entry.
  Rejected on cost; it was the recommendation on technical grounds alone.
- **OV with DigiCert KeyLocker or SSL.com eSigner.** $700 to $1,700 a year, signature caps below the
  ~1,800 signatures a year this project needs (about 40 per release), and long-lived secrets in GitHub.
- **EV.** Costs more and has given no SmartScreen advantage since 2024.
- **A PFX as a CI secret.** Not possible since the CA/B Forum rule of June 2023: the key must stay in
  an HSM.
- **SignPath Foundation** (free for open source). Shows SignPath Foundation as the publisher, while
  the consent screen of the junis-hosted registration shows junis GmbH.
- **Stay unsigned.** ASR rule C1DB55AB blocks fresh releases, AllSigned blocks the install script,
  and app-control customers need a hash rule per version.

## What signing changes, whichever option

- ASR C1DB55AB ("advanced protection against ransomware") does not block a validly signed file.
- ASR 01443614 ("prevalence, age, or trusted list") has no exemption for signed files; Microsoft
  says signing alone does not exempt a file. Its documented path exclusions stay.
- SmartScreen still warns about new files until they build reputation, with any certificate type.
- The 72-hour update hold (`UpdateService.MinimumReleaseAge`) therefore stays for 1.0.0, and 0.x
  clients have it built in anyway. Shorten it only after measuring a signed release in the field.

## Consequences

- The leaf certificate lives 72 hours and changes daily. Customers cannot pin it as a certificate
  Allow indicator or a TrustedPublisher entry, so AllSigned stays a blocker for the install script.
  App Control customers write a publisher rule on the Artifact Signing chain plus the certificate
  profile's EKU; verify the rule with `New-CIPolicy -Level Publisher` before documenting it.
- The identity validation happens once per subscription, in the portal only: 1 to 20 business days,
  three attempts, and a failure is final. It needs a paid subscription (no free, trial or sponsored
  one), Handelsregister data no older than 12 months that matches the public record exactly, two
  junis.de mailboxes, and a Verified ID check of a named representative of junis GmbH.
- Runbook: never delete or recreate the certificate profile (that changes the EKU customers' rules
  may use), never delete the identity validation, and renew it before it expires. Renewal opens 60
  days before expiry (Renew under Identity validations, Identity Verifier role) and is a full
  re-validation of 1 to 20 business days; a lapsed validation stops certificate issuance, and every
  release then fails at signing. On Basic a deleted profile stays "Deleting" for an undocumented
  time and still counts against the one profile per type, so a replacement needs a new account in
  the same subscription, which reuses the identity validation. Never sign a
  release with a "Public Trust Test" profile: its signatures die with the 72-hour leaf.
- One certificate profile signs every junis product, not one per product: a customer's App Control
  rule on its EKU therefore allows everything junis signs with it.
- The signing identity is a user-assigned managed identity with *Artifact Signing Certificate Profile
  Signer* on the profile only, reached over OIDC from the `release` environment. The junis-hosted
  registration never gets a credential ([SECURITY.md](../../SECURITY.md)).
- Switch to the OV certificate in Key Vault if customers need a certificate they can pin, or if the
  identity validation fails for good.
