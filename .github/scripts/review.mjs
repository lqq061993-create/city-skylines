#!/usr/bin/env node
/*!
 * IndustryMaster 自动代码审查
 * 由 .github/workflows/code-review.yml 调用：读本次改动的 diff，交给模型审查，
 * 结果回贴到 PR（PR 触发）或汇总 issue（push 到 main 触发）。
 *
 * 模型来源：仓库 secret REVIEW_API_KEY + 变量 REVIEW_API_BASE / REVIEW_MODEL
 * （任意 OpenAI 兼容端点，或 REVIEW_API_FORMAT=anthropic 走 Anthropic 协议）。
 * 没配 key 或模型不可达时只记一行日志并跳过，不让 CI 变红——审查是辅助，不该阻塞提交。
 */
import { execFileSync } from 'node:child_process';
import { readFileSync } from 'node:fs';

const GH_TOKEN = process.env.GITHUB_TOKEN;
const KEY = (process.env.REVIEW_API_KEY || '').trim();
const BASE = (process.env.REVIEW_API_BASE || '').trim().replace(/\/+$/, '');
const MODEL = (process.env.REVIEW_MODEL || '').trim();
const FORMAT = (process.env.REVIEW_API_FORMAT || 'openai').trim().toLowerCase();
const REPO = process.env.GITHUB_REPOSITORY;
const API = 'https://api.github.com';
const MARKER = '<!-- industry-master-auto-review -->';
const MAX_DIFF = 60000;

const SYSTEM = [
  '你是《城市：天际线 II》模组 IndustryMaster 的代码审查者。只看本次 diff，不臆测未改动的代码。',
  '',
  '硬性约束（违反即为问题）：',
  '1. 金钱累加必须走 DirectTradePolicy.AddSaturated(int,int)，禁止裸加减。',
  '2. Harmony 补丁在 Install 里必须先校验元数据（方法存在、字段/枚举类型匹配），不匹配抛 MissingMemberException，由 Mod.cs 的 try/catch 捕获后降级，不能让模组整体失效。',
  '3. 反射得到的枚举下标（如 CompanySection.ResultType.Profit）必须运行时解析并做边界检查，不得硬编码。',
  '4. 用户可见文本必须通过 Mod.Text(中文, English) 提供中英双语。',
  '5. 修改游戏 ECS 数据必须考虑 NativeArray / ComponentData 生命周期与 System 执行顺序。',
  '6. 本仓库无法在 CI 编译（游戏程序集不能分发），不要报"可能编译不过"这类无法验证的猜测；只报逻辑、绑定假设、空引用、溢出、单位换算、设置项是否真正接入等问题。',
  '',
  '输出格式（Markdown，中文）：',
  '## 审查结论',
  '一句话结论 + 风险等级（低/中/高）。',
  '## 问题',
  '按严重度排列，每条：文件:行 → 问题 → 为什么错 → 建议改法。没有就写"未发现"。',
  '## 建议',
  '可选的小改进，最多三条。不要重复 diff 内容，不要客套。',
].join('\n');

const run = (cmd, args) => execFileSync(cmd, args, { encoding: 'utf8', maxBuffer: 64 * 1024 * 1024 });

const api = async (path, init = {}) => {
  const res = await fetch(API + path, {
    ...init,
    headers: {
      Authorization: 'Bearer ' + GH_TOKEN,
      Accept: 'application/vnd.github+json',
      'X-GitHub-Api-Version': '2022-11-28',
      'Content-Type': 'application/json',
      ...(init.headers || {}),
    },
  });
  if (!res.ok) throw new Error(path + ' -> ' + res.status + ' ' + (await res.text()).slice(0, 300));
  return res.status === 204 ? null : res.json();
};

const event = JSON.parse(readFileSync(process.env.GITHUB_EVENT_PATH, 'utf8'));
const HEAD = event.after || (event.pull_request && event.pull_request.head.sha) || '';

function changedDiff() {
  const paths = ['*.cs', '*.mjs', '*.json', '*.md', '*.yml', '*.sh', '*.ps1'];
  if (event.pull_request) {
    return run('git', ['diff', '--unified=3', '--no-color', event.pull_request.base.sha, event.pull_request.head.sha, '--', ...paths]);
  }
  const before = event.before && !/^0+$/.test(event.before) ? event.before : event.after + '~1';
  return run('git', ['diff', '--unified=3', '--no-color', before, event.after, '--', ...paths]);
}

const jsonOrNull = (raw) => {
  try {
    return JSON.parse(raw);
  } catch {
    return null;
  }
};

const brief = (raw) => String(raw).replace(/\s+/g, ' ').trim().slice(0, 200);

function completionText(body) {
  const content = body && body.choices && body.choices[0] && body.choices[0].message && body.choices[0].message.content;
  return typeof content === 'string' && content.trim() ? content.trim() : null;
}

async function callOpenAICompatible(userContent) {
  const res = await fetch(BASE + '/chat/completions', {
    method: 'POST',
    headers: { Authorization: 'Bearer ' + KEY, 'Content-Type': 'application/json' },
    body: JSON.stringify({
      model: MODEL,
      temperature: 0.2,
      max_tokens: 1800,
      messages: [{ role: 'system', content: SYSTEM }, { role: 'user', content: userContent }],
    }),
  });
  const raw = await res.text();
  if (!res.ok) throw new Error(MODEL + ': HTTP ' + res.status + ' ' + brief(raw));
  const text = completionText(jsonOrNull(raw));
  if (!text) throw new Error(MODEL + ': 响应不是预期的 chat completion -> ' + brief(raw));
  return { text, label: MODEL };
}

async function callAnthropic(userContent) {
  const res = await fetch(BASE + '/v1/messages', {
    method: 'POST',
    headers: { 'x-api-key': KEY, 'anthropic-version': '2023-06-01', 'Content-Type': 'application/json' },
    body: JSON.stringify({ model: MODEL, max_tokens: 1800, system: SYSTEM, messages: [{ role: 'user', content: userContent }] }),
  });
  const raw = await res.text();
  if (!res.ok) throw new Error(MODEL + ': HTTP ' + res.status + ' ' + brief(raw));
  const body = jsonOrNull(raw);
  const text = body && Array.isArray(body.content) ? body.content.map((part) => part.text || '').join('').trim() : '';
  if (!text) throw new Error(MODEL + ': 响应不是预期的 messages 结构 -> ' + brief(raw));
  return { text, label: MODEL };
}

async function publish(markdown, label) {
  const footer = '\n\n---\n<sub>由 GitHub Actions 自动生成（' + label + '），仅供参考。CI 无法编译本模组（游戏程序集不可分发）。</sub>\n' + MARKER;
  if (event.pull_request) {
    await api('/repos/' + REPO + '/issues/' + event.pull_request.number + '/comments', {
      method: 'POST',
      body: JSON.stringify({ body: markdown + footer }),
    });
    console.log('已评论 PR #' + event.pull_request.number);
    return;
  }
  const issues = await api('/repos/' + REPO + '/issues?labels=auto-review&state=open&per_page=1');
  let number = issues[0] && issues[0].number;
  if (!number) {
    const created = await api('/repos/' + REPO + '/issues', {
      method: 'POST',
      body: JSON.stringify({
        title: '自动代码审查记录',
        body: 'push 到 main 后，本 issue 中机器人那条评论会被更新为本次审查结果。',
        labels: ['auto-review'],
      }),
    });
    number = created.number;
  }
  const comments = await api('/repos/' + REPO + '/issues/' + number + '/comments?per_page=100');
  const existing = comments.find((c) => c.body.indexOf(MARKER) >= 0);
  const body = '### ' + String(HEAD).slice(0, 7) + ' 的审查\n\n' + markdown + footer;
  if (existing) {
    await api('/repos/' + REPO + '/issues/comments/' + existing.id, { method: 'PATCH', body: JSON.stringify({ body }) });
    console.log('已更新 issue #' + number + ' 的审查评论');
  } else {
    await api('/repos/' + REPO + '/issues/' + number + '/comments', { method: 'POST', body: JSON.stringify({ body }) });
    console.log('已在 issue #' + number + ' 留言');
  }
}

const diff = changedDiff();
if (!diff.trim()) {
  console.log('本次没有可审查的改动，跳过。');
} else {
  const clipped = diff.length > MAX_DIFF ? diff.slice(0, MAX_DIFF) + '\n[diff 已截断，共 ' + diff.length + ' 字符]' : diff;
  const userContent = '仓库：' + REPO + '\n提交：' + HEAD + '\n\n---- DIFF ----\n' + clipped;
  if (!(KEY && BASE && MODEL)) {
    console.log('未配置 REVIEW_API_KEY / REVIEW_API_BASE / REVIEW_MODEL，本次跳过审查。');
    process.exit(0);
  }
  console.log('使用 API：' + BASE + ' / ' + MODEL + ' (' + FORMAT + ')');
  let result = null;
  try {
    result = FORMAT === 'anthropic' ? await callAnthropic(userContent) : await callOpenAICompatible(userContent);
  } catch (error) {
    const reason = String(error.message).slice(0, 400);
    console.warn('审查模型不可用，本次跳过：' + reason);
    if (event.pull_request) {
      await publish('### ⚠️ 本次自动审查未执行\n\n审查模型不可用，这份改动没有经过审查。\n\n```\n' + reason + '\n```', '模型不可用');
    }
    process.exit(0);
  }
  await publish(result.text, result.label);
  console.log('\n' + result.text);
}
