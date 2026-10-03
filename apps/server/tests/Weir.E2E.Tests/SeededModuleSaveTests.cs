using System.Text.RegularExpressions;
using Microsoft.Playwright;
using Weir.E2E.Tests.Harness;
using Weir.E2E.Tests.Support;
using static Microsoft.Playwright.Assertions;

namespace Weir.E2E.Tests;

public sealed partial class SeededModuleSaveTests(E2EServer server) : E2ETestBase(server)
{
    [E2EFact]
    public async Task Saved_state_persists_across_settings_and_processing()
    {
        // Sibling of the data folder, not inside it: Weir refuses its own data folder as a library folder (#723),
        // and a real install never keeps media there either.
        var home = new DirectoryInfo(Server.Home);
        var tvMediaRoot = Path.Combine(home.Parent!.FullName, $"{home.Name}-tv-media");
        var tvWatch = Path.Combine(tvMediaRoot, "tv-watch-missing");
        var tvOutput = Path.Combine(tvMediaRoot, "tv-output");
        Directory.CreateDirectory(tvOutput);
        try
        {
            var page = await NewPageAsync();

            await Navigation.EnsureSignedInAsync(page, BaseUrl);

            // The setup wizard reopens from System › About's "This Weir" card. There is no display
            // density setting, in the wizard or on the page.
            await Navigation.OpenSidebarAsync(page, "System");
            await Expect(page.GetByTestId("suite-settings-global")).ToBeVisibleAsync();
            await page.GetByTestId("suite-settings-open-setup-wizard").ClickAsync();
            await Expect(page).ToHaveURLAsync(SetupWizardUrl());
            await Expect(page.GetByRole(AriaRole.Heading, new() { Name = "Set up Weir" })).ToBeVisibleAsync();
            await Expect(page.GetByText("Display density", new() { Exact = false })).ToHaveCountAsync(0);
            await page.GetByTestId("setup-wizard-skip").ClickAsync();
            await Expect(page).ToHaveURLAsync(ShellUrl());
            await Expect(page.GetByTestId("processing-page")).ToBeVisibleAsync();
            await Expect(page.Locator("html")).Not.ToHaveAttributeAsync("data-mm-density", AnyValue());

            // Workflows is where setup opens.
            await Navigation.OpenTabAsync(page, "Workflows", "File paths");
            var libraries = page.GetByTestId("processing-libraries-section");
            await Expect(libraries).ToBeVisibleAsync();
            // Every workflow says whether it is Weir only or linked to a media manager.
            await Expect(libraries.GetByTestId("workflow-kind-badge").First).ToBeVisibleAsync();
            await libraries.GetByRole(AriaRole.Button, new() { Name = "Edit", Exact = true }).Nth(1).ClickAsync();
            var form = page.GetByTestId("processing-library-form");
            await form.GetByRole(AriaRole.Textbox, new() { Name = "Watched folder" }).FillAsync(tvWatch);
            await form.GetByRole(AriaRole.Textbox, new() { Name = "Output folder" }).FillAsync(tvOutput);
            await page.GetByTestId("processing-library-save").ClickAsync();
            await Expect(form).ToHaveCountAsync(0);
            await Navigation.OpenSidebarAsync(page, "Dashboard");
            await Expect(page.GetByTestId("processing-page")).ToBeVisibleAsync();
            await Navigation.OpenTabAsync(page, "Workflows", "File paths");
            await Expect(page.GetByTestId("processing-libraries-section")).ToContainTextAsync(tvWatch);
        }
        finally
        {
            Directory.Delete(tvMediaRoot, recursive: true);
        }
    }

    [GeneratedRegex(".*/setup-wizard")]
    private static partial Regex SetupWizardUrl();

    [GeneratedRegex(@".*/(?:$|[?#])")]
    private static partial Regex ShellUrl();

    [GeneratedRegex(".*")]
    private static partial Regex AnyValue();
}
