// Demo data for manual testing: a Saudi school, an Egyptian center and an independent teacher, with
// classes, courses, quizzes, assignments, attendance, parents, announcements and notifications.
// Everything goes through the API, so business rules apply. Safe to run again (existing data is reused).
//
//   node tools/demo-seed/seed.mjs [apiBaseUrl]       default http://localhost:5057
//   env: DEMO_ADMIN_USER / DEMO_ADMIN_PASSWORD        platform admin (default demo.admin / Demo@12345)
//
// All demo users share the password Demo@12345 (see README.md for the list).

const BASE = (process.argv[2] ?? 'http://localhost:5057').replace(/\/$/, '');
const PASSWORD = 'Demo@12345';
const ADMIN_USER = process.env.DEMO_ADMIN_USER ?? 'demo.admin';
const ADMIN_PASSWORD = process.env.DEMO_ADMIN_PASSWORD ?? PASSWORD;
const STUDENT = 1, TEACHER = 2, PARENT = 4;

// ---- HTTP client with cookies and rate-limit retry -----------------------------------------------------------
class Client {
  constructor(name) { this.name = name; this.cookies = new Map(); }
  async req(method, path, body) {
    for (let attempt = 0; ; attempt++) {
      const headers = { 'Content-Type': 'application/json' };
      if (this.cookies.size) headers.Cookie = [...this.cookies].map(([k, v]) => `${k}=${v}`).join('; ');
      const res = await fetch(BASE + path, { method, headers, body: body === undefined ? undefined : JSON.stringify(body) });
      for (const c of res.headers.getSetCookie?.() ?? []) {
        const [pair] = c.split(';'); const i = pair.indexOf('=');
        this.cookies.set(pair.slice(0, i), pair.slice(i + 1));
      }
      if (res.status === 429 && attempt < 10) { await sleep(7000); continue; }
      const text = await res.text();
      let json; try { json = JSON.parse(text); } catch { json = text; }
      return { status: res.status, body: json, data: json?.data };
    }
  }
  async ok(method, path, body) {
    const r = await this.req(method, path, body);
    if (r.status >= 300) throw new Error(`${this.name}: ${method} ${path} → ${r.status} ${JSON.stringify(r.body).slice(0, 500)}`);
    return r.data;
  }
  get(p) { return this.ok('GET', p); }
  post(p, b = {}) { return this.ok('POST', p, b); }
  put(p, b = {}) { return this.ok('PUT', p, b); }
}
const sleep = ms => new Promise(r => setTimeout(r, ms));
const log = (...a) => console.log(...a);

const users = new Map();
/** Registers the user if needed and returns a signed-in client (with .id and .userName). */
async function user(userName, type) {
  if (users.has(userName)) return users.get(userName);
  const c = new Client(userName);
  // Demo students are 15 (school age): they study and use codes, and a linked parent pays for them.
  const birthYear = type === STUDENT ? new Date().getFullYear() - 15 : null;
  const reg = await c.req('POST', '/api/Auth/register', { userName, email: `${userName}@demo.aqlam.local`, password: PASSWORD, confirmPassword: PASSWORD, userType: type, birthYear });
  if (reg.status >= 300 && reg.status !== 400 && reg.status !== 409) throw new Error(`register ${userName}: ${JSON.stringify(reg.body)}`);
  const login = await c.req('POST', '/api/Auth/login', { userName, password: PASSWORD });
  if (login.status !== 200) throw new Error(`login ${userName} failed (${login.status}). If the user exists with another password, delete it or rename the demo user.`);
  c.id = (await c.get('/api/Auth/me')).id;
  c.userName = userName;
  users.set(userName, c);
  return c;
}

// ---- Lookups ------------------------------------------------------------------------------------------------
const lookups = {};
async function loadLookups(c) {
  lookups.curricula = await c.get('/api/Lookups/curricula');
  lookups.subjects = await c.get('/api/Lookups/subjects');
}
async function gradeOf(c, curriculumCode, stageIndex, gradeIndex) {
  const curriculum = lookups.curricula.find(x => x.code === curriculumCode);
  const stages = await c.get(`/api/Lookups/education-stages?curriculumId=${curriculum.id}`);
  const stage = stages[Math.min(stageIndex, stages.length - 1)];
  const grades = await c.get(`/api/Lookups/education-grades?stageId=${stage.id}`);
  return { curriculumId: curriculum.id, stageId: stage.id, gradeId: grades[Math.min(gradeIndex, grades.length - 1)].id };
}
const subjectId = (...names) => {
  const s = lookups.subjects.find(x => names.some(n => x.nameEn.toLowerCase().includes(n) || x.nameAr.includes(n)));
  return s?.id ?? lookups.subjects[0].id;
};

// ---- Building blocks ----------------------------------------------------------------------------------------
/** Academic year, weighted gradebooks (typed and linked to quizzes/homework) and a published first term. Safe to re-run. */
async function academicsDemo(owner, org) {
  const api = `/api/orgs/${org}`;
  if ((await owner.get(`${api}/academic-years`)).length) { log('  = academic year (exists)'); return; }

  const day = offset => new Date(Date.now() + offset * 86400000).toISOString().slice(0, 10);
  const year = await owner.post(`${api}/academic-years`, {
    name: '1448 هـ', startDate: day(-70), endDate: day(220),
    terms: [
      { name: 'الفصل الدراسي الأول', startDate: day(-70), endDate: day(60) },
      { name: 'الفصل الدراسي الثاني', startDate: day(70), endDate: day(220) },
    ],
  });
  const termId = year.terms[0].id;

  for (const summary of await owner.get(`${api}/classes`)) {
    const detail = await owner.get(`${api}/classes/${summary.id}`);
    // Arabic stays empty on purpose: a report card simply leaves out subjects with no grades.
    for (const subject of detail.assignments.filter(a => a.courseStatus === 'Published')) {
      const url = `${api}/classes/${summary.id}/assignments/${subject.id}/gradebook`;
      const book = await owner.get(`${url}?termId=${termId}`);
      const work = await owner.post(`${url}/categories`, { termId, name: 'أعمال السنة', weightPercent: 40 });
      const midterm = await owner.post(`${url}/categories`, { termId, name: 'اختبار منتصف الفصل', weightPercent: 20 });
      await owner.post(`${url}/categories`, { termId, name: 'الاختبار النهائي', weightPercent: 40 }); // not taken yet

      for (const lesson of book.linkableLessons.slice(0, 2))
        await owner.post(`${url}/categories/${work.id}/items`, { title: lesson.title, maxPoints: 10, sourceLessonId: lesson.lessonId });
      const participation = await owner.post(`${url}/categories/${work.id}/items`, { title: 'المشاركة', maxPoints: 10 });
      const exam = await owner.post(`${url}/categories/${midterm.id}/items`, { title: 'اختبار منتصف الفصل', maxPoints: 20 });

      await owner.put(`${url}/items/${participation.id}/scores`, { scores: detail.students.map((s, i) => ({ userId: s.userId, score: 10 - i })) });
      await owner.put(`${url}/items/${exam.id}/scores`, { scores: detail.students.map((s, i) => ({ userId: s.userId, score: 19 - i * 4 })) });
    }
    log(`  ✓ gradebook: ${summary.name}`);
  }
  await owner.put(`${api}/academic-years/terms/${termId}/report-cards`, { published: true });
  log('  ✓ first term report cards published');
}

async function organization(admin, { name, slug, type, countryCode, ownerUserName }) {
  const r = await admin.req('POST', '/api/platform/organizations', { name, slug, type, countryCode, ownerUserName });
  if (r.status === 200) log(`  + ${type} ${slug}`);
  else if (r.status === 409 || r.status === 400) log(`  = ${type} ${slug} (exists)`);
  else throw new Error(`create org ${slug}: ${JSON.stringify(r.body)}`);
}

async function member(owner, org, userName, type, role, gender) {
  const c = await user(userName, type);
  const r = await owner.req('POST', `/api/orgs/${org}/members`, { userName, roles: [role] });
  if (r.status >= 300 && r.status !== 409) throw new Error(`add ${userName}: ${JSON.stringify(r.body)}`);
  if (gender) await owner.put(`/api/orgs/${org}/members/${c.id}/gender`, { gender });
  return c;
}

const video = (title, n = 1) => ({ title, type: 'Video', videoUrl: `https://www.youtube.com/watch?v=${['dQw4w9WgXcQ', 'M7lc1UVf-VE', 'aqz-KE-bpKQ'][n % 3]}`, durationInMinutes: 12, isFreePreview: n === 0, isPublished: true, unlockDaysAfterEnrollment: 0, requirePreviousLessonCompletion: false });
const article = title => ({ title, type: 'Article', content: `<p>${title}: ملخص الدرس مع أمثلة محلولة وتمارين للمراجعة.</p>`, isFreePreview: false, isPublished: true, unlockDaysAfterEnrollment: 0, requirePreviousLessonCompletion: false });
const quizLesson = title => ({ title, type: 'Quiz', isFreePreview: false, isPublished: true, unlockDaysAfterEnrollment: 0, requirePreviousLessonCompletion: false });
const homework = title => ({ title, type: 'Assignment', content: `<p>${title}: حل التمارين وارفع رابط الحل.</p>`, isFreePreview: false, isPublished: true, unlockDaysAfterEnrollment: 0, requirePreviousLessonCompletion: false });

/** Question bank items; correct answers are remembered so students can answer realistically. */
const answerKey = new Map();
async function questionBank(teacher, org, cls) {
  const existing = await teacher.get(`/api/orgs/${org}/questions`);
  const specs = [
    { type: 'SingleChoice', text: 'ما ناتج 7 × 8 ؟', options: [['54', false], ['56', true], ['64', false], ['48', false]] },
    { type: 'TrueFalse', text: 'مجموع زوايا المثلث 180 درجة.', options: [['صح', true], ['خطأ', false]] },
    { type: 'MultipleChoice', text: 'أي الأعداد التالية أعداد أولية؟', options: [['2', true], ['9', false], ['11', true], ['15', false]] },
    { type: 'ShortAnswer', text: 'ما اسم العالم الذي وضع قوانين الحركة الثلاثة؟', accepted: ['نيوتن', 'Newton'] },
    { type: 'SingleChoice', text: 'What is 3/4 as a percentage?', options: [['34%', false], ['75%', true], ['43%', false]] },
  ];
  const ids = [];
  for (const q of specs) {
    let found = existing.find(e => e.text === q.text);
    if (!found) {
      found = await teacher.post(`/api/orgs/${org}/questions`, {
        type: q.type, text: q.text, explanation: null, points: 1, difficulty: 2,
        curriculumId: cls.curriculumId, gradeId: cls.gradeId, subjectId: cls.subjectId,
        options: q.options?.map(([text, isCorrect]) => ({ text, isCorrect })) ?? [], acceptedAnswers: q.accepted ?? [],
      });
    }
    answerKey.set(found.id, { type: q.type, correct: (found.options ?? []).filter(o => o.isCorrect).map(o => o.id), text: q.accepted?.[0] });
    ids.push(found.id);
  }
  return ids;
}

/** Fills an empty course with two units (video, article, quiz, homework) and publishes it. */
async function buildAndPublish(staff, publisher, org, courseId, subjectName, questionIds, { dueInDays = 5 } = {}) {
  let draft = await staff.get(`/api/orgs/${org}/courses/${courseId}/draft`);
  if (!draft.chapters?.length) {
    await staff.put(`/api/orgs/${org}/courses/${courseId}/curriculum`, {
      expectedVersion: draft.curriculumVersion,
      syllabusRequirements: 'دفتر وقلم، والالتزام بحل الواجبات في وقتها.',
      chapters: [
        { title: `${subjectName}: الوحدة الأولى`, lessons: [video('مقدمة الوحدة', 0), article('ملخص الدرس الأول'), quizLesson('اختبار قصير 1')] },
        { title: `${subjectName}: الوحدة الثانية`, lessons: [video('شرح الدرس الثاني', 1), homework('واجب الوحدة الثانية'), video('مراجعة عامة', 2)] },
      ],
    });
    draft = await staff.get(`/api/orgs/${org}/courses/${courseId}/draft`);
  }
  const lessons = draft.chapters.flatMap(ch => ch.lessons);
  const quiz = lessons.find(l => l.type === 'Quiz');
  const hw = lessons.find(l => l.type === 'Assignment');
  await staff.put(`/api/orgs/${org}/courses/${courseId}/lessons/${quiz.id}/quiz`, {
    timeLimitMinutes: 15, maxAttempts: 3, passPercent: 60, shuffleQuestions: true, shuffleOptions: true, showAnswersAfterSubmit: true,
    questions: questionIds.map(questionId => ({ questionId, points: 2 })),
  });
  await staff.put(`/api/orgs/${org}/courses/${courseId}/lessons/${hw.id}/assignment`, {
    maxPoints: 20, dueAtUtc: new Date(Date.now() + dueInDays * 86400000).toISOString(), acceptLateSubmissions: true,
  });
  if (draft.status === 'Draft') await publisher.put(`/api/orgs/${org}/courses/${courseId}/status`, { status: 'Published' });
  return { lessons, quiz, hw };
}

/** A public course of an independent teacher, built, published and listed. Reuses one with the same title. Returns its id. */
async function publicCourse(teacher, org, bank, { title, subtitle, curriculumId, stageId, gradeId, subjectId: subject, pricingType, price, codeActivationOnly }) {
  let course = (await teacher.get(`/api/orgs/${org}/courses`)).find(c => c.title === title);
  if (!course) {
    course = await teacher.post(`/api/orgs/${org}/courses/draft`, { title });
    await teacher.put(`/api/orgs/${org}/courses/${course.id}/identity`, { id: course.id, title, curriculumId, stageId, gradeId, subjectId: subject });
    await teacher.put(`/api/orgs/${org}/courses/${course.id}/commercials`, { id: course.id, pricingType, price, codeActivationOnly, accessExpiryPolicy: 'Unlimited', accessDurationDays: null });
    await teacher.put(`/api/orgs/${org}/courses/${course.id}/media`, { id: course.id, subtitle, thumbnailUrl: null, promoVideoUrl: null });
  }
  await buildAndPublish(teacher, teacher, org, course.id, title.split(':')[0], bank);
  await teacher.put(`/api/orgs/${org}/courses/${course.id}/visibility`, { visibility: 'Public' });
  return course.id;
}

/** A student studies: watches/reads, takes the quiz (good or weak), hands in homework. */
async function study(student, org, courseId, plan, { strong = true, submit = true, watchAll = true } = {}) {
  const outline = await student.get(`/api/orgs/${org}/learn/courses/${courseId}`);
  const lessons = outline.chapters.flatMap(ch => ch.lessons);
  for (const [i, l] of lessons.entries()) {
    if (!watchAll && i > 1) break;
    if (l.type === 'Video') await student.req('POST', `/api/orgs/${org}/learn/lessons/${l.id}/progress`, { positionSeconds: 600, watchedPercent: strong || i === 0 ? 95 : 55 });
    if (l.type === 'Article') await student.req('POST', `/api/orgs/${org}/learn/lessons/${l.id}/progress`, { markCompleted: true });
  }
  const summary = await student.get(`/api/orgs/${org}/learn/lessons/${plan.quiz.id}/quiz`);
  if (summary.attemptsUsed === 0) {
    const attempt = await student.post(`/api/orgs/${org}/learn/lessons/${plan.quiz.id}/quiz/attempts`);
    const answers = attempt.questions.map((q, i) => {
      const key = answerKey.get(q.questionId);
      const right = strong || i % 2 === 0;
      if (key?.type === 'ShortAnswer') return { questionId: q.questionId, selectedOptionIds: [], textAnswer: right ? 'نيوتُن' : 'أينشتاين' };
      const correct = key?.correct ?? [];
      const wrong = q.options.map(o => o.id).filter(id => !correct.includes(id)).slice(0, 1);
      return { questionId: q.questionId, selectedOptionIds: right ? correct : wrong, textAnswer: null };
    });
    await student.put(`/api/orgs/${org}/learn/attempts/${attempt.id}/answers`, { answers });
    await student.post(`/api/orgs/${org}/learn/attempts/${attempt.id}/submit`);
  }
  if (submit) {
    const hw = await student.get(`/api/orgs/${org}/learn/lessons/${plan.hw.id}/assignment`);
    if (!hw.submission)
      await student.put(`/api/orgs/${org}/learn/lessons/${plan.hw.id}/assignment/submission`, {
        text: 'تم حل جميع التمارين، والرابط فيه صور الحل.', links: ['https://drive.example.com/homework/' + student.userName],
      });
  }
}

/** Grades the queue: most get a score, one is returned for revision, one stays pending. */
async function grade(teacher, org, courseId) {
  const pending = await teacher.get(`/api/orgs/${org}/courses/${courseId}/submissions?pending=true`);
  for (const [i, s] of pending.entries()) {
    if (i === pending.length - 1 && pending.length > 2) continue; // leave one waiting
    const body = i === 1
      ? { score: null, feedback: 'أحسنت البداية، أكمل السؤال الثالث وأعد الرفع.', returnForRevision: true }
      : { score: 14 + (i % 6), feedback: 'عمل ممتاز، انتبه لترتيب الخطوات.', returnForRevision: false };
    await teacher.put(`/api/orgs/${org}/submissions/${s.id}/grade`, body);
  }
}

async function classGroup(owner, org, input) {
  const existing = (await owner.get(`/api/orgs/${org}/classes`)).find(c => c.name === input.name);
  if (existing) return existing;
  log(`  + class ${input.name}`);
  return owner.post(`/api/orgs/${org}/classes`, input);
}

async function assign(owner, org, classId, subject, subjectName, teacher) {
  const detail = await owner.get(`/api/orgs/${org}/classes/${classId}`);
  const existing = detail.assignments.find(a => a.subjectName === subjectName);
  if (existing) return existing;
  return owner.post(`/api/orgs/${org}/classes/${classId}/assignments`, { subjectId: subject, subjectName, teacherUserId: teacher.id });
}

/** Attendance for past school days; a few absences/lates per class (parents get alerts). */
async function attendanceHistory(taker, org, classId, students, { days = 14, workDays = [0, 1, 2, 3, 4], startTimes = [null] } = {}) {
  const statuses = ['Present', 'Present', 'Present', 'Present', 'Present', 'Present', 'Absent', 'Present', 'Late', 'Present', 'Present', 'Excused'];
  let n = 0;
  for (let d = days; d >= 1; d--) {
    const day = new Date(Date.now() - d * 86400000);
    if (!workDays.includes(day.getUTCDay())) continue;
    const date = day.toISOString().slice(0, 10);
    for (const startTime of startTimes) {
      const marks = students.map((s, i) => {
        const status = statuses[(d * 7 + i * 5 + n) % statuses.length];
        return { userId: s.id, status, note: status === 'Excused' ? 'عذر طبي' : status === 'Late' ? 'تأخر الباص' : null };
      });
      await taker.put(`/api/orgs/${org}/classes/${classId}/attendance`, { date, startTime, marks });
      n++;
    }
  }
}

async function announce(sender, org, a) {
  const list = await sender.get(`/api/orgs/${org}/announcements`);
  if (list.some(x => x.title === a.title)) return;
  await sender.post(`/api/orgs/${org}/announcements`, a);
}

async function linkParent(owner, org, child, parent) {
  const r = await owner.req('POST', `/api/orgs/${org}/members/${child.id}/parents`, { parentUserName: parent.userName });
  if (r.status >= 300 && r.status !== 409) throw new Error(`link parent: ${JSON.stringify(r.body)}`);
}

// =============================================================================================================
async function main() {
  log(`Seeding demo data via ${BASE}\n`);
  const admin = new Client('admin');
  const login = await admin.req('POST', '/api/Auth/login', { userName: ADMIN_USER, password: ADMIN_PASSWORD });
  if (login.status !== 200)
    throw new Error(`Platform admin login failed. Start the API with Platform__BootstrapAdmin__UserName=${ADMIN_USER} and Platform__BootstrapAdmin__Password=<password> (see README).`);
  await loadLookups(admin);

  // ---- 1. Saudi school ----------------------------------------------------------------------------------
  log('1) Saudi school: rowad-school');
  const S = 'rowad-school';
  const sOwner = await user('rowad.owner', TEACHER);
  if (process.env.DEMO_ONLY === 'academics') { // add Phase 4 data to a database seeded earlier
    await academicsDemo(sOwner, 'rowad-school');
    return;
  }
  await organization(admin, { name: 'مدارس الرواد الأهلية', slug: S, type: 'School', countryCode: 'SA', ownerUserName: 'rowad.owner' });
  await sOwner.put(`/api/orgs/${S}/settings`, { name: 'مدارس الرواد الأهلية', timeZone: 'Asia/Riyadh', calendar: 'Both', weekStartDay: 'Sunday', logoUrl: null, primaryColor: '#0E7C66' });

  const coord = await member(sOwner, S, 'rowad.coord', TEACHER, 'AcademicCoordinator', 'Male');
  const tAhmed = await member(sOwner, S, 't.ahmed', TEACHER, 'Teacher', 'Male');
  const tKhalid = await member(sOwner, S, 't.khalid', TEACHER, 'Teacher', 'Male');
  const tFatimah = await member(sOwner, S, 't.fatimah', TEACHER, 'Teacher', 'Female');
  const tNoura = await member(sOwner, S, 't.noura', TEACHER, 'Teacher', 'Female');
  await member(sOwner, S, 'a.salem', TEACHER, 'Assistant', 'Male');
  const boys = [];
  for (const u of ['s.omar', 's.yousef', 's.faisal', 's.abdullah', 's.majed', 's.turki']) boys.push(await member(sOwner, S, u, STUDENT, 'Student', 'Male'));
  const girls = [];
  for (const u of ['s.sara', 's.lama', 's.reem', 's.hessa', 's.jana', 's.dana']) girls.push(await member(sOwner, S, u, STUDENT, 'Student', 'Female'));

  const branches = await sOwner.get(`/api/orgs/${S}/branches`);
  const boysCampus = branches.find(b => b.name === 'مجمع البنين') ?? await sOwner.post(`/api/orgs/${S}/branches`, { name: 'مجمع البنين', gender: 'Boys', address: 'حي النرجس، الرياض' });
  const girlsCampus = branches.find(b => b.name === 'مجمع البنات') ?? await sOwner.post(`/api/orgs/${S}/branches`, { name: 'مجمع البنات', gender: 'Girls', address: 'حي الياسمين، الرياض' });

  const g7 = await gradeOf(admin, 'SA_NATIONAL', 1, 0); // intermediate, first grade
  const g8 = await gradeOf(admin, 'SA_NATIONAL', 1, 1);
  const classes = [
    { name: 'أول متوسط - أ', gender: 'Boys', branch: boysCampus, grade: g7, students: boys.slice(0, 3), homeroom: tAhmed, math: tAhmed, science: tKhalid },
    { name: 'أول متوسط - ب', gender: 'Girls', branch: girlsCampus, grade: g7, students: girls.slice(0, 3), homeroom: tFatimah, math: tNoura, science: tFatimah },
    { name: 'ثاني متوسط - أ', gender: 'Boys', branch: boysCampus, grade: g8, students: boys.slice(3), homeroom: tKhalid, math: tAhmed, science: tKhalid },
    { name: 'ثاني متوسط - ب', gender: 'Girls', branch: girlsCampus, grade: g8, students: girls.slice(3), homeroom: tNoura, math: tNoura, science: tFatimah },
  ];
  const mathId = subjectId('math', 'رياضيات'), scienceId = subjectId('science', 'علوم'), arabicId = subjectId('arabic', 'عربي', 'لغتي');

  for (const c of classes) {
    const cls = await classGroup(sOwner, S, {
      name: c.name, kind: 'Section', gender: c.gender, branchId: c.branch.id, curriculumId: c.grade.curriculumId, gradeId: c.grade.gradeId,
      homeroomTeacherUserId: c.homeroom.id, capacity: 30, schedule: [],
    });
    c.id = cls.id;
    await sOwner.post(`/api/orgs/${S}/classes/${c.id}/students`, { userNames: c.students.map(s => s.userName) });

    // Math and science are built and published; Arabic stays a draft (shows the teacher's to-do).
    const math = await assign(sOwner, S, c.id, mathId, 'الرياضيات', c.math);
    const science = await assign(sOwner, S, c.id, scienceId, 'العلوم', c.science);
    await assign(sOwner, S, c.id, arabicId, 'لغتي', c.gender === 'Boys' ? tKhalid : tNoura);

    const bank = await questionBank(c.math, S, { curriculumId: c.grade.curriculumId, gradeId: c.grade.gradeId, subjectId: mathId });
    const mathPlan = await buildAndPublish(c.math, sOwner, S, math.courseId, 'الرياضيات', bank.slice(0, 4));
    const sciencePlan = await buildAndPublish(c.science, sOwner, S, science.courseId, 'العلوم', [bank[1], bank[3]], { dueInDays: -2 }); // overdue → late work

    for (const [i, s] of c.students.entries()) {
      await study(s, S, math.courseId, mathPlan, { strong: i !== 1, submit: true });
      await study(s, S, science.courseId, sciencePlan, { strong: i === 0, submit: i !== 2, watchAll: i === 0 });
    }
    await grade(c.math, S, math.courseId);
    await attendanceHistory(c.homeroom, S, c.id, c.students);
    log(`  ✓ ${c.name}: ${c.students.length} students, 3 subjects, attendance, quizzes, homework`);
  }

  // Parents: a father with a son and a daughter (siblings, one shared phone family), a mother via code.
  const pQahtani = await user('p.qahtani', PARENT);
  await linkParent(sOwner, S, boys[0], pQahtani);  // s.omar
  await linkParent(sOwner, S, girls[0], pQahtani); // s.sara
  const pMother = await user('p.mother.lama', PARENT);
  const children = await pMother.get('/api/me/children');
  if (!children.some(c => c.userId === girls[1].id)) {
    const invite = await sOwner.post(`/api/orgs/${S}/members/${girls[1].id}/parent-invites`);
    await pMother.post('/api/me/children', { code: invite.code });
  }
  await user('p.new', PARENT); // has no child yet: use the code printed below
  const spareCode = await sOwner.post(`/api/orgs/${S}/members/${boys[1].id}/parent-invites`);

  // Attendance today for the first class (one absence → fresh alert to the father).
  const today = new Date().toISOString().slice(0, 10);
  await tAhmed.put(`/api/orgs/${S}/classes/${classes[0].id}/attendance`, {
    date: today, marks: classes[0].students.map((s, i) => ({ userId: s.id, status: i === 0 ? 'Absent' : 'Present', note: null })),
  });

  await announce(sOwner, S, { title: 'بداية الفصل الدراسي الثاني', body: 'نرحب بأبنائنا الطلاب وبناتنا الطالبات، ونتمنى لكم فصلاً موفقاً. يبدأ الدوام الساعة 6:45 صباحاً.', scope: 'Organization', audience: ['Students', 'Parents', 'Staff'], sendEmail: false });
  await announce(sOwner, S, { title: 'اجتماع المعلمين', body: 'اجتماع المعلمين يوم الأربعاء بعد الحصة السادسة في قاعة الاجتماعات.', scope: 'Organization', audience: ['Staff'], sendEmail: false });
  await announce(coord, S, { title: 'اختبارات منتصف الفصل - أول متوسط', body: 'تبدأ اختبارات منتصف الفصل يوم الأحد القادم. الجدول مرفق في المنصة.', scope: 'Grade', gradeId: g7.gradeId, audience: ['Students', 'Parents'], sendEmail: false });
  await announce(tAhmed, S, { title: 'واجب الرياضيات', body: 'تذكير: آخر موعد لتسليم واجب الوحدة الثانية يوم الخميس.', scope: 'Class', classGroupId: classes[0].id, audience: ['Students', 'Parents'], sendEmail: false });

  // A student who asked to join with the school code and waits for approval.
  const codes = await sOwner.get(`/api/orgs/${S}/join-codes`);
  const joinCode = codes.find(c => c.isActive && c.requiresApproval) ?? await sOwner.post(`/api/orgs/${S}/join-codes`, { role: 'Student', requiresApproval: true, maxUses: 100 });
  const pending = await user('s.pending', STUDENT);
  await pending.req('POST', '/api/me/join', { code: joinCode.code });

  await academicsDemo(sOwner, S);

  // ---- 2. Egyptian center -------------------------------------------------------------------------------
  log('2) Egyptian center: tamayoz-center');
  const C = 'tamayoz-center';
  const cOwner = await user('tamayoz.owner', TEACHER);
  await organization(admin, { name: 'مركز التميز التعليمي', slug: C, type: 'Center', countryCode: 'EG', ownerUserName: 'tamayoz.owner' });
  const tMostafa = await member(cOwner, C, 't.mostafa', TEACHER, 'Teacher', 'Male');
  const centerStudents = [];
  for (const [u, g] of [['s.mariam', 'Female'], ['s.ali', 'Male'], ['s.hana', 'Female'], ['s.karim', 'Male']]) centerStudents.push(await member(cOwner, C, u, STUDENT, 'Student', g));
  const cBranches = await cOwner.get(`/api/orgs/${C}/branches`);
  const nasr = cBranches.find(b => b.name === 'فرع مدينة نصر') ?? await cOwner.post(`/api/orgs/${C}/branches`, { name: 'فرع مدينة نصر', gender: 'Mixed', address: 'مدينة نصر، القاهرة' });
  const g3s = await gradeOf(admin, 'EG_NATIONAL', 2, 2); // secondary, third grade
  const physicsGroup = await classGroup(cOwner, C, {
    name: 'فيزياء 3 ثانوي - سبت/اثنين 5م', kind: 'Group', gender: 'Mixed', branchId: nasr.id, curriculumId: g3s.curriculumId, gradeId: g3s.gradeId,
    homeroomTeacherUserId: tMostafa.id, capacity: 40,
    schedule: [{ day: 'Saturday', startTime: '17:00', endTime: '18:30' }, { day: 'Monday', startTime: '17:00', endTime: '18:30' }],
  });
  await cOwner.post(`/api/orgs/${C}/classes/${physicsGroup.id}/students`, { userNames: centerStudents.map(s => s.userName) });
  const physics = await assign(cOwner, C, physicsGroup.id, subjectId('physics', 'فيزياء'), 'الفيزياء', tMostafa);
  const cBank = await questionBank(tMostafa, C, { curriculumId: g3s.curriculumId, gradeId: g3s.gradeId, subjectId: subjectId('physics', 'فيزياء') });
  const physicsPlan = await buildAndPublish(tMostafa, cOwner, C, physics.courseId, 'الفيزياء', [cBank[3], cBank[1]]);
  for (const [i, s] of centerStudents.entries()) await study(s, C, physics.courseId, physicsPlan, { strong: i % 2 === 0, submit: i !== 3 });
  await grade(tMostafa, C, physics.courseId);
  await attendanceHistory(tMostafa, C, physicsGroup.id, centerStudents, { days: 21, workDays: [6, 1], startTimes: ['17:00'] });
  const pHassan = await user('p.hassan', PARENT);
  await linkParent(cOwner, C, centerStudents[1], pHassan); // s.ali
  await announce(cOwner, C, { title: 'تغيير موعد حصة السبت', body: 'حصة الفيزياء يوم السبت القادم الساعة 6 مساءً بدلاً من 5.', scope: 'Class', classGroupId: physicsGroup.id, audience: ['Students', 'Parents'], sendEmail: false });
  log('  ✓ physics group with schedule, per-meeting attendance, quiz, homework, parent');

  // ---- 3. Independent teacher with a public course --------------------------------------------------------
  log('3) Independent teacher: t.huda (public course)');
  const tHuda = await user('t.huda', TEACHER);
  const hudaOrg = (await tHuda.get('/api/me/memberships')).find(m => m.type === 'Personal').slug;
  const hudaCourses = await tHuda.get(`/api/orgs/${hudaOrg}/courses`);
  let qudrat = hudaCourses.find(c => c.title === 'قدرات - القسم الكمي');
  if (!qudrat) {
    qudrat = await tHuda.post(`/api/orgs/${hudaOrg}/courses/draft`, { title: 'قدرات - القسم الكمي' });
    const tp = await gradeOf(admin, 'SA_ADMISSION_TESTS', 0, 0);
    await tHuda.put(`/api/orgs/${hudaOrg}/courses/${qudrat.id}/identity`, { id: qudrat.id, title: 'قدرات - القسم الكمي', ...tp, subjectId: mathId });
    await tHuda.put(`/api/orgs/${hudaOrg}/courses/${qudrat.id}/commercials`, { id: qudrat.id, pricingType: 'Free', price: null, codeActivationOnly: false, accessExpiryPolicy: 'Unlimited', accessDurationDays: null });
    await tHuda.put(`/api/orgs/${hudaOrg}/courses/${qudrat.id}/media`, { id: qudrat.id, subtitle: 'تأسيس شامل للقسم الكمي مع اختبارات محاكية', thumbnailUrl: null, promoVideoUrl: null });
  }
  const hBank = await questionBank(tHuda, hudaOrg, {});
  const qPlan = await buildAndPublish(tHuda, tHuda, hudaOrg, qudrat.id, 'الكمي', hBank);
  await tHuda.put(`/api/orgs/${hudaOrg}/courses/${qudrat.id}/visibility`, { visibility: 'Public' });
  for (const s of [girls[0], centerStudents[0]]) {
    await s.req('POST', `/api/orgs/${hudaOrg}/learn/courses/${qudrat.id}/enroll`);
    await study(s, hudaOrg, qudrat.id, qPlan, { strong: true, submit: false, watchAll: false });
  }
  log(`  ✓ public free course in ${hudaOrg}, 2 self-enrolled students`);

  // ---- 4. Marketplace: public teacher pages, a paid course, a code-only course, picks --------------------
  log('4) Marketplace');
  await tHuda.put(`/api/orgs/${hudaOrg}/public-profile`, {
    publicName: 'أ. هدى العتيبي', headline: 'معلمة رياضيات وقدرات للمرحلة الثانوية', city: 'الرياض', gender: 'Female', isListed: true,
    bio: 'أدرّس الرياضيات منذ اثني عشر عامًا، وأساعد الطالبات على الاستعداد لاختبارَي القدرات والتحصيلي بخطة أسبوعية واضحة واختبارات محاكية.',
  });
  const tahsiliTp = await gradeOf(admin, 'SA_ADMISSION_TESTS', 0, 1);
  const tahsili = await publicCourse(tHuda, hudaOrg, hBank, {
    title: 'تحصيلي رياضيات: مراجعة شاملة', subtitle: 'كل موضوعات الرياضيات في الاختبار التحصيلي مع حل نماذج سابقة',
    ...tahsiliTp, subjectId: mathId, pricingType: 'Paid', price: 149, codeActivationOnly: false,
  });
  const algebraTp = await gradeOf(admin, 'SA_NATIONAL', 2, 0);
  const algebra = await publicCourse(tHuda, hudaOrg, hBank, {
    title: 'الجبر للصف الأول الثانوي', subtitle: 'للطالبات المسجلات عبر المكتبات المعتمدة', ...algebraTp, subjectId: mathId,
    pricingType: 'Paid', price: 99, codeActivationOnly: true,
  });
  const batches = await tHuda.get(`/api/orgs/${hudaOrg}/courses/${algebra}/code-batches`);
  let demoCodes = [];
  if (!batches.length) {
    demoCodes = (await tHuda.post(`/api/orgs/${hudaOrg}/courses/${algebra}/code-batches`, { name: 'مكتبة النور - فرع العليا', quantity: 20, expiresAtUtc: null })).codes;
  }

  const tMajed = await user('t.majed', TEACHER);
  const majedOrg = (await tMajed.get('/api/me/memberships')).find(m => m.type === 'Personal').slug;
  const mBank = await questionBank(tMajed, majedOrg, {});
  const physicsTp = await gradeOf(admin, 'SA_NATIONAL', 2, 1);
  await publicCourse(tMajed, majedOrg, mBank, {
    title: 'فيزياء ثاني ثانوي: الحركة والقوى', subtitle: 'شرح مبسط بالتجارب والأمثلة المحلولة', ...physicsTp,
    subjectId: subjectId('physics', 'فيزياء'), pricingType: 'Free', price: null, codeActivationOnly: false,
  });
  await tMajed.put(`/api/orgs/${majedOrg}/public-profile`, {
    publicName: 'أ. ماجد الحربي', headline: 'معلم فيزياء للمرحلة الثانوية', city: 'جدة', gender: 'Male', isListed: true,
    bio: 'معلم فيزياء، أركّز على الفهم قبل الحفظ، وأربط كل درس بتجربة يمكن تنفيذها في البيت.',
  });

  // Picks by أقلام (not a ranking). Fee rates are left for the platform owner to set.
  const picks = await admin.get('/api/platform/marketplace/featured');
  for (const [slug, subject] of [[hudaOrg, mathId], [majedOrg, subjectId('physics', 'فيزياء')]]) {
    if (!picks.some(p => p.teacher.slug === slug && p.subjectId === subject))
      await admin.post('/api/platform/marketplace/featured', { slug, subjectId: subject, order: 1 });
  }
  log(`  ✓ listed teachers ${hudaOrg} and ${majedOrg}; paid course ${tahsili}; code-only course ${algebra}`);
  if (demoCodes.length) log(`  activation codes for "الجبر للصف الأول الثانوي" (shown once): ${demoCodes.slice(0, 5).join(', ')}`);

  log(`
Done. All demo users use the password ${PASSWORD}
  Platform admin : ${ADMIN_USER}
  School         : rowad.owner (owner), rowad.coord (coordinator), t.ahmed t.khalid t.fatimah t.noura (teachers), a.salem (assistant)
                   students s.omar s.yousef s.faisal s.abdullah s.majed s.turki (boys) · s.sara s.lama s.reem s.hessa s.jana s.dana (girls)
                   s.pending (waiting for approval, join code ${joinCode.code})
  Parents        : p.qahtani (s.omar + s.sara) · p.mother.lama (s.lama) · p.hassan (s.ali, center) · p.new (no child yet)
                   family code for s.yousef → ${spareCode.code} (sign in as p.new, My children, enter it)
  Center (EG)    : tamayoz.owner, t.mostafa · students s.mariam s.ali s.hana s.karim
  Independent    : t.huda (listed; free Qudrat course, paid Tahsili course, code-only algebra course) · t.majed (listed; free physics)`);
}

main().catch(err => { console.error('\n❌ ' + err.message); process.exitCode = 1; });
