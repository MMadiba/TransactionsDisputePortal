import { expect, test, type APIRequestContext, type Page } from '@playwright/test'

const password = process.env.E2E_PASSWORD ?? 'Demo#Passw0rd'
const customer = 'sipho.dlamini@mmbank.co.za'
const agent = 'naledi.khumalo@mmbank.co.za'

async function signIn(page: Page, email: string) {
  await page.goto('/login')
  await page.getByLabel('Email').fill(email)
  await page.getByLabel('Password').fill(password)
  await page.getByRole('button', { name: 'Sign in' }).click()
}

/** Finds a transaction the customer can still dispute, so the test can run again and again on the same data. */
async function findDisputableTransaction(request: APIRequestContext) {
  const token = await request.post('/api/auth/token', { data: { email: customer, password } })
  const { accessToken } = (await token.json()) as { accessToken: string }
  const headers = { Authorization: `Bearer ${accessToken}` }

  const list = await request.get('/api/transactions?pageSize=100', { headers })
  for (const t of ((await list.json()) as { items: { id: string; merchant: string; status: string; latestDispute: unknown }[] }).items) {
    if (t.status !== 'Posted' || t.latestDispute) continue
    const detail = (await (await request.get(`/api/transactions/${t.id}`, { headers })).json()) as { canDispute: boolean }
    if (detail.canDispute) return t
  }
  throw new Error(`No disputable transactions left for ${customer}. Reset the demo data (see the README).`)
}

test('a customer disputes a transaction, an agent upholds it, and the customer sees the outcome', async ({ page, browser, request }) => {
  const transaction = await findDisputableTransaction(request)

  // Customer raises the dispute.
  await signIn(page, customer)
  await expect(page.getByRole('heading', { name: 'Your transactions' })).toBeVisible()
  await page.goto(`/transactions/${transaction.id}`)
  await page.getByLabel('What went wrong?').selectOption('DuplicateCharge')
  await page.getByLabel('Tell us more').fill('The same amount came off my card twice within a minute.')
  await page.getByRole('button', { name: 'Submit dispute' }).click()

  await expect(page.getByText("Thanks, we've received your dispute.")).toBeVisible()
  const reference = (await page.getByRole('heading', { name: /^Dispute DSP-/ }).textContent())!.replace('Dispute ', '')

  // An agent, in a separate browser session, picks it up and upholds it.
  const agentContext = await browser.newContext()
  const agentPage = await agentContext.newPage()
  await signIn(agentPage, agent)
  await agentPage.getByRole('link', { name: reference }).click()
  await agentPage.getByRole('button', { name: 'Start review' }).click()
  await expect(agentPage.getByText("You're reviewing this dispute.")).toBeVisible()
  await agentPage.getByLabel('Uphold — refund the customer').check()
  await agentPage.getByLabel('Note to the customer').fill('We confirmed the duplicate charge. The refund is on its way.')
  await agentPage.getByRole('button', { name: 'Close dispute' }).click()
  await expect(agentPage.getByText('Upheld').first()).toBeVisible()
  await agentContext.close()

  // Back as the customer: the outcome is there, and the agent shows up as "Disputes team", not by name.
  await page.reload()
  await expect(page.getByText('Resolved in your favour').first()).toBeVisible()
  await expect(page.getByText('We confirmed the duplicate charge. The refund is on its way.').first()).toBeVisible()
  await expect(page.getByText('by Disputes team').first()).toBeVisible()
  await expect(page.getByText('Naledi Khumalo')).toHaveCount(0)
})
