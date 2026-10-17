# Privacy

Entra PIM Manager is a desktop app that runs on your Windows device. junis GmbH publishes it.

- **The app sends nothing to junis.** That holds for the junis-hosted App Registration too: Microsoft issues its tokens to the app on your device, not to junis.
- **Tokens, configuration and logs stay on your device**, in your Windows profile. What exactly: [What lands on your machine](docs/user-guide.md#what-lands-on-your-machine).
- **Sign-in and the Microsoft Graph and Azure Resource Manager calls go to Microsoft**, as the signed-in user.
- **The update check contacts GitHub Releases** once a day; switch it off under **Settings → Updates**. **Check for updates** and the network check contact GitHub only when you click them.
- **The junis-hosted admin-consent link ends on a page on GitHub Pages.** After the consent, Microsoft sends the admin's browser there, with the tenant id and the result in the address. The page shows the result and sends nothing anywhere; GitHub, which hosts it, sees the visit.
- **Terms:** the MIT [LICENSE](LICENSE).
