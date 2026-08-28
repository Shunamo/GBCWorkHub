/**
 * WorkLogListGlassView.xaml 한글 복구 + TFS 배지 조정
 *   node tools/fix_glass_korean.js
 *
 * 주의: 이 XAML의 한글은 PowerShell/일부 편집기로 건드리면 ??? 로 깨집니다.
 *       한글 수정은 반드시 Node(\\u 이스케이프)로만 하세요.
 */
const fs = require('fs');
const path = require('path');

const file = path.join(
  __dirname,
  '../src/GBCWorkHub.UI/Views/WorkLog/WorkLogListGlassView.xaml'
);
let s = fs.readFileSync(file, 'utf8');
if (s.charCodeAt(0) === 0xfeff) s = s.slice(1);

const U = {
  reset: '\uCD08\uAE30\uD654',
  collapse: '\uC811\uAE30',
  deploy: '\uBC30\uD3EC',
  writeStatus: '\uC791\uC131\uC0C1\uD0DC',
  period: '\uAE30\uAC04',
  start: '\uC2DC\uC791',
  end: '\uC885\uB8CC',
  excel: 'Excel \uAC00\uC838\uC624\uAE30',
  newLog: '\uC0C8 \uC5C5\uBB34\uAE30\uB85D',
  site: '\uC0AC\uC774\uD2B8 \uC120\uD0DD',
  search:
    '\uD2F0\uCF13\u00B7\uBA54\uB274\u00B7\uD504\uB85C\uC81D\uD2B8\u00B7\uC18C\uC2A4\u00B7\uCF54\uBA58\uD2B8 \uAC80\uC0C9',
  filter: '\uD544\uD130',
  empty: '\uD56D\uBAA9\uC774 \uC5C6\uC2B5\uB2C8\uB2E4.',
  tfsImport: 'TFS \uAC00\uC838\uC624\uAE30',
  expand: '\uD3BC\uCE58\uAE30/\uC811\uAE30',
  prev: '\uC774\uC804',
  next: '\uB2E4\uC74C',
};

function replaceOnce(re, to, label) {
  const m = s.match(re);
  if (!m) throw new Error('FAIL ' + label);
  s = s.replace(re, to);
}

replaceOnce(
  /(Content=")[^"]*("\s+Padding="10,4"[^>]*\r?\n\s*Command="\{Binding ResetFiltersCommand\}")/,
  '$1' + U.reset + '$2',
  'reset'
);
replaceOnce(
  /(Command="\{Binding ToggleFilterCommand\}" ToolTip=")[^"]*(">\s*\r?\n\s*<TextBlock Text="&#x2039;")/,
  '$1' + U.collapse + '$2',
  'collapse'
);
replaceOnce(
  /(DockPanel\.Dock="Left" Text=")[^"]*("\s*\r?\n\s*Style="\{StaticResource GlassFilterExpanderTitle\}" \/>\s*\r?\n\s*<TextBlock Text="\{Binding FilterDeploy\}")/,
  '$1' + U.deploy + '$2',
  'deploy'
);
replaceOnce(
  /(DockPanel\.Dock="Left" Text=")[^"]*("\s*\r?\n\s*Style="\{StaticResource GlassFilterExpanderTitle\}" \/>\s*\r?\n\s*<TextBlock Text="\{Binding FilterWriteStatus\}")/,
  '$1' + U.writeStatus + '$2',
  'writeStatus'
);
replaceOnce(
  /(DockPanel\.Dock="Left" Text=")[^"]*("\s*\r?\n\s*Style="\{StaticResource GlassFilterExpanderTitle\}" \/>\s*\r?\n\s*<TextBlock Text="\{Binding FilterDateSummary\}")/,
  '$1' + U.period + '$2',
  'period'
);

// From/To placeholders: first occurrence before FilterFromText, second before FilterToText
{
  const a = s.indexOf('Binding="{Binding FilterFromText}"');
  const b = s.indexOf('Binding="{Binding FilterToText}"');
  if (a < 0 || b < 0) throw new Error('date placeholders');
  function setNearestText(beforePos, value) {
    const key = 'Text="';
    const ti = s.lastIndexOf(key, beforePos);
    const start = ti + key.length;
    const end = s.indexOf('"', start);
    s = s.slice(0, start) + value + s.slice(end);
  }
  setNearestText(a, U.start);
  // FilterToText index may shift if start string length differs — re-find
  const b2 = s.indexOf('Binding="{Binding FilterToText}"');
  setNearestText(b2, U.end);
}

replaceOnce(/(ToolTip=")Excel [^"]*(")/, '$1' + U.excel + '$2', 'excel');
replaceOnce(
  /(Command="\{Binding NewWorkLogCommand\}"\s*\r?\n\s*ToolTip=")[^"]*(")/,
  '$1' + U.newLog + '$2',
  'newLog'
);
replaceOnce(
  /(Command="\{Binding ToggleSitePickerCommand\}"\s*\r?\n\s*ToolTip=")[^"]*(")/,
  '$1' + U.site + '$2',
  'site'
);
replaceOnce(
  /(<TextBlock Text=")[^"]*(" FontSize="\{StaticResource FontSize\.Base\}" Foreground="#94A3B8"\s*\r?\n\s*Margin="12,0,0,0")/,
  '$1' + U.search + '$2',
  'search'
);
replaceOnce(
  /(Command="\{Binding ToggleFilterCommand\}"\s*\r?\n\s*ToolTip=")[^"]*(">\s*\r?\n\s*<Image Source="\{StaticResource FilterSlidersIcon\}")/,
  '$1' + U.filter + '$2',
  'filter'
);
replaceOnce(
  /(<TextBlock Text=")[^"]*(" FontSize="\{StaticResource FontSize\.Base\}" FontWeight="SemiBold"\s*\r?\n\s*Foreground="\{StaticResource WlGlassMuted\}" HorizontalAlignment="Center" \/>)/,
  '$1' + U.empty + '$2',
  'empty'
);
replaceOnce(
  /(GlassAccentButton\}" Content=")TFS [^"]*(")/,
  '$1' + U.tfsImport + '$2',
  'tfsImport'
);
replaceOnce(
  /(Command="\{Binding ToggleExpandCommand\}"\s*\r?\n\s*ToolTip=")[^"]*(")/,
  '$1' + U.expand + '$2',
  'expand'
);
replaceOnce(
  /(Command="\{Binding PrevPageCommand\}" ToolTip=")[^"]*(")/,
  '$1' + U.prev + '$2',
  'prev'
);
replaceOnce(
  /(Command="\{Binding NextPageCommand\}" ToolTip=")[^"]*(")/,
  '$1' + U.next + '$2',
  'next'
);

replaceOnce(
  /<Border Background="#0078D4" CornerRadius="[^"]*" Width="22" Height="20" Padding="0"\s*\r?\n\s*SnapsToDevicePixels="True">\s*\r?\n\s*<TextBlock Text="TFS" FontSize="[^"]*" FontWeight="Bold"\s*\r?\n\s*Foreground="White" FontFamily="Segoe UI"\s*\r?\n(?:\s*Margin="[^"]*"\s*\r?\n)?\s*VerticalAlignment="Center" HorizontalAlignment="Center" \/>/,
  `<Border Background="#0078D4" CornerRadius="3" Width="22" Height="20" Padding="0"
                                                SnapsToDevicePixels="True">
                                            <TextBlock Text="TFS" FontSize="9" FontWeight="Bold"
                                                       Foreground="White" FontFamily="Segoe UI"
                                                       Margin="0,-1,0,0"
                                                       VerticalAlignment="Center" HorizontalAlignment="Center" />`,
  'tfsBadge'
);

const body = s.replace(/\r?\n/g, '\r\n');
fs.writeFileSync(file, '\uFEFF' + body, 'utf8');

const left = (body.match(/\?{2,}/g) || []).length;
console.log('remaining ??', left);
for (const [k, v] of Object.entries(U)) {
  if (!body.includes(v)) console.log('MISSING', k);
}
console.log('TFS9', /Text="TFS" FontSize="9"/.test(body));
if (left > 0) process.exit(2);
console.log('OK');
