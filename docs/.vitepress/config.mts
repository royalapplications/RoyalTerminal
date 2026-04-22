import { defineConfig } from "vitepress";

import { apiPackageGroups } from "./api-packages.mjs";

const guideItems = [
  { text: "Overview", link: "/" },
  { text: "Getting Started", link: "/articles/getting-started" },
  { text: "Architecture", link: "/articles/architecture" },
  { text: "Package Guide", link: "/articles/packages" },
  { text: "Embedding In Avalonia", link: "/articles/avalonia-control" },
  { text: "Sessions, Profiles, And Settings", link: "/articles/sessions-profiles-and-settings" },
  { text: "Transports And Remote Access", link: "/articles/transports" },
  { text: "Terminal Engine And Screen State", link: "/articles/vt-modes" },
  { text: "Rendering, Text, And Graphics", link: "/articles/rendering-native" },
  { text: "Shader Support", link: "/articles/shaders" },
  { text: "Applying Shaders", link: "/articles/shaders-applying" },
  { text: "Skia Runtime Effect Shaders", link: "/articles/shaders-skia-runtime-effect" },
  { text: "Ghostty/Shadertoy Shader Compatibility", link: "/articles/shaders-ghostty-shadertoy" },
  { text: "Windows Terminal HLSL Shader Compatibility", link: "/articles/shaders-windows-terminal-hlsl" },
  { text: "Compiler-Backed HLSL Shader Packages", link: "/articles/shaders-full-hlsl-packages" },
  { text: "Ghostty Integration", link: "/articles/ghostty-integration" },
  { text: "Samples And Tooling", link: "/articles/samples-tooling" },
  { text: "Build, Test, And Release", link: "/articles/build-test-release" },
  { text: "Troubleshooting", link: "/articles/troubleshooting" }
];

const apiSidebarItems = [
  { text: "Overview", link: "/api/" }
];

export default defineConfig({
  title: "RoyalTerminal",
  description:
    ".NET 10 terminal platform for Avalonia with multi-transport sessions, managed and native VT processors, and modular rendering/runtime packages.",
  base: "/RoyalTerminal/",
  cleanUrls: true,
  lastUpdated: true,
  head: [
    ["meta", { name: "theme-color", content: "#0f766e" }],
    ["meta", { property: "og:type", content: "website" }],
    ["meta", { property: "og:title", content: "RoyalTerminal" }],
    [
      "meta",
      {
        property: "og:description",
        content:
          ".NET 10 terminal platform for Avalonia with multi-transport sessions, managed and native VT processors, and modular rendering/runtime packages."
      }
    ]
  ],
  markdown: {
    lineNumbers: true
  },
  themeConfig: {
    logo: "/assets/royalterminal-mark.svg",
    nav: [
      { text: "Guide", link: "/articles/getting-started" },
      { text: "Packages", link: "/articles/packages" },
      { text: "API", link: "/api/" },
      { text: "GitHub", link: "https://github.com/royalapplications/RoyalTerminal" }
    ],
    sidebar: {
      "/articles/": [
        {
          text: "Guide",
          items: guideItems
        }
      ],
      "/api/": [
        {
          text: "API Reference",
          items: apiSidebarItems
        },
        ...apiPackageGroups.map((group) => ({
          text: group.text,
          items: group.packages.map((pkg) => ({
            text: pkg.packageId,
            link: `/api/${pkg.slug}/`
          }))
        }))
      ],
      "/": [
        {
          text: "Guide",
          items: guideItems
        },
        {
          text: "API Reference",
          items: apiSidebarItems
        }
      ]
    },
    outline: {
      level: [2, 3],
      label: "On this page"
    },
    editLink: {
      pattern: "https://github.com/royalapplications/RoyalTerminal/edit/main/docs/:path",
      text: "Edit this page on GitHub"
    },
    docFooter: {
      prev: "Previous page",
      next: "Next page"
    },
    search: {
      provider: "local"
    },
    socialLinks: [
      { icon: "github", link: "https://github.com/royalapplications/RoyalTerminal" }
    ],
    footer: {
      message: "MIT Licensed",
      copyright: "Copyright Royal Apps Contributors"
    }
  }
});
