# A fresh sign-in for every activation

Entra PIM Manager makes activating a role take three clicks. Unless your tenant asks for it,
nothing in those three clicks makes you prove again that it is you: the sign-in you did earlier,
and the tokens Windows keeps from it, are enough for the role to become active. Whoever sits at
your unlocked PC, or holds your stolen tokens, gets the same three clicks.

This page shows how to make every activation ask for a fresh sign-in. The activation panel points
out each role that does not, with "⚠ This role activates without asking you to sign in again" and
a link here.

## Why PIM's MFA setting is not enough

PIM's role setting **On activation, require multifactor authentication** checks that the session
has an MFA claim, not that you just signed in. Microsoft says so: "Users might not be prompted for
multifactor authentication if they authenticated with strong credentials or provided multifactor
authentication earlier in the session"
([PIM role settings](https://learn.microsoft.com/entra/id-governance/privileged-identity-management/pim-how-to-change-default-settings#on-activation-require-multifactor-authentication)).

Only a **Conditional Access authentication context** with the sign-in frequency **Every time**
asks for a fresh sign-in on each activation, and Entra checks and logs it. Every tenant that can use
PIM is licensed for it: PIM needs Microsoft Entra ID P2 or Entra ID Governance, and both come with
or require P1, which covers Conditional Access.

## Set it up

You need Conditional Access Administrator and Privileged Role Administrator; you can activate both
with Entra PIM Manager.

1. Under **Conditional Access → Authentication contexts**, create a context, for example
   "PIM activation", and tick **Publish to apps**.
2. Create a Conditional Access policy:
   - **Target resources:** that authentication context.
   - **Users:** all users, or the group of your eligible admins. Not the directory role: nobody
     holds it yet at the moment of activation. Exclude your emergency access accounts.
   - **Grant:** require an authentication strength, ideally phishing-resistant MFA.
   - **Session:** sign-in frequency **Every time**.

   Turn it on for a pilot group first. A policy in report-only mode does not ask anyone anything.
3. In the PIM settings of each role (Entra roles, groups and Azure resource roles alike), set
   **On activation, require Microsoft Entra Conditional Access authentication context** to that
   context.

Microsoft's walkthrough:
[On activation, require Conditional Access authentication context](https://learn.microsoft.com/entra/id-governance/privileged-identity-management/pim-how-to-change-default-settings#on-activation-require-microsoft-entra-conditional-access-authentication-context).

## Answer it with a glance or a touch

A fresh sign-in on every activation only stays painless if the sign-in is. **Microsoft Entra
passkey on Windows** stores a passkey for your admin account in the PC's Windows Hello, without
joining or registering the device, so the prompt becomes a face, fingerprint or PIN, and it counts
as phishing-resistant MFA. An admin allows it once under **Authentication methods → Passkey
(FIDO2)** with a passkey profile that permits Windows Hello; each admin then registers the passkey
under [Security info](https://mysignins.microsoft.com/security-info). Microsoft's guide:
[Microsoft Entra passkey on Windows](https://learn.microsoft.com/entra/identity/authentication/how-to-authentication-entra-passkeys-on-windows).
An account that already has Windows Hello for Business on that PC uses that instead.

## What changes in Entra PIM Manager

- The hint disappears from the role. Instead, "ℹ This activation may require additional
  verification (MFA)" tells you that Windows will ask during the activation. The app sees only
  that the role asks for a context, not the Conditional Access policy behind it: make sure that
  policy is on, targets the context, and uses **Every time**, or the context asks for nothing.
- Each activation brings up the Windows sign-in, for directory roles, PIM for Groups and Azure
  resource roles alike. Entra waives the prompt for further activations within ten minutes, so
  ticking three subscriptions asks once.
- An account added with a device-code sign-in cannot satisfy an authentication context. Remove it
  and add it again with the normal sign-in.
- For most Azure resource roles the app cannot read the role's settings with your permissions, so
  it shows no hint either way. The setting applies all the same.
