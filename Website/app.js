import { baseLayerLuminance, StandardLuminance } from 'https://unpkg.com/@fluentui/web-components';

const setTheme = () => {
  const isDark = window.matchMedia("(prefers-color-scheme: dark)").matches;
  baseLayerLuminance.setValueFor(
    document.documentElement, 
    isDark ? StandardLuminance.DarkMode : StandardLuminance.LightMode
  );
};

setTheme();
window.matchMedia('(prefers-color-scheme: dark)').addEventListener('change', setTheme);