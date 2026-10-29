<template>
  <div class="dashboard">
    <h1>Shuttle Fleet Dashboard</h1>

    <div v-if="loading" class="state loading">Loading dashboard data…</div>
    <div v-if="error" class="state error">{{ error }}</div>

    <section class="config">
      <p>Refresh interval: {{ refreshIntervalMs }} ms</p>
    </section>

    <section class="rack">
      <h2>Rack map</h2>
      <div class="lanes">
        <div v-for="lane in laneIds" :key="lane" class="lane">
          <span class="lane-id">{{ lane }}</span>
          <div v-for="shuttle in shuttlesOnLane(lane)" :key="shuttle.shuttleId" class="marker">
            {{ shuttle.shuttleId }} — {{ shuttle.state }}
            <span v-if="shuttle.pauseRequested" class="pause-flag">(pause requested)</span>
          </div>
        </div>
      </div>
    </section>

    <section class="shuttles">
      <h2>Shuttles</h2>
      <ul>
        <li v-for="s in shuttles" :key="s.shuttleId">
          <button @click="selected = s.shuttleId">{{ s.shuttleId }}</button>
          {{ s.state }} @ {{ s.position }}
          <span v-if="s.pauseRequested">(pause requested)</span>
        </li>
      </ul>
    </section>

    <section class="controls">
      <h2>Controls{{ selected ? ' for ' + selected : '' }}</h2>
      <button :disabled="!selected || busy" @click="send('Pause')">Pause</button>
      <button :disabled="!selected || busy" @click="send('Resume')">Resume</button>
      <button :disabled="!selected || busy" @click="send('Acknowledge')">Acknowledge</button>
      <span v-if="busy" class="busy">Sending…</span>
      <div v-if="commandError" class="error">{{ commandError }}</div>
      <div v-if="commandResult" class="result">{{ commandResult }}</div>
    </section>

    <section class="tasks">
      <h2>Task queue</h2>
      <ul>
        <li v-for="t in tasks" :key="t.taskId">{{ t.taskId }} — {{ t.state }} ({{ t.assignedShuttleId || 'unassigned' }})</li>
      </ul>
    </section>

    <section class="alarms">
      <h2>Alarms</h2>
      <ul>
        <li v-for="a in alarms" :key="a.shuttleId + a.code">
          {{ a.shuttleId }} — {{ a.code }} ({{ a.severity }})
          <span v-if="a.acknowledgedAt">acknowledged {{ a.acknowledgedAt }}</span>
          <span v-else>unacknowledged</span>
        </li>
      </ul>
    </section>
  </div>
</template>

<script setup>
import { ref, onMounted, onBeforeUnmount, computed } from 'vue'

const loading = ref(true)
const error = ref(null)
const laneIds = ref([])
const refreshIntervalMs = ref(0)
const shuttles = ref([])
const tasks = ref([])
const alarms = ref([])
const selected = ref(null)
const busy = ref(false)
const commandError = ref(null)
const commandResult = ref(null)

let timer = null

function shuttlesOnLane(lane) {
  return shuttles.value.filter(s => s.position === lane)
}

async function fetchJson(url) {
  const res = await fetch(url)
  if (!res.ok) throw new Error(`Request failed: ${res.status}`)
  return res.json()
}

async function refresh() {
  try {
    const [s, t, a] = await Promise.all([
      fetchJson('/api/dashboard/shuttles'),
      fetchJson('/api/dashboard/tasks'),
      fetchJson('/api/dashboard/alarms'),
    ])
    shuttles.value = s
    tasks.value = t
    alarms.value = a
    error.value = null
  } catch (e) {
    error.value = e.message
  } finally {
    loading.value = false
  }
}

async function loadConfig() {
  const c = await fetchJson('/api/dashboard/config')
  laneIds.value = c.laneIds || []
  refreshIntervalMs.value = c.refreshIntervalMs || 0
}

async function send(kind) {
  busy.value = true
  commandError.value = null
  commandResult.value = null
  try {
    const res = await fetch('/api/dashboard/operator-command', {
      method: 'POST',
      headers: { 'Content-Type': 'application/json' },
      body: JSON.stringify({ shuttleId: selected.value, kind, reason: null }),
    })
    if (!res.ok) throw new Error(`Command failed: ${res.status}`)
    commandResult.value = `${kind} request accepted for publishing`
    await refresh()
  } catch (e) {
    commandError.value = e.message
  } finally {
    busy.value = false
  }
}

onMounted(async () => {
  try {
    await loadConfig()
    await refresh()
    const interval = refreshIntervalMs.value > 0 ? refreshIntervalMs.value : 2000
    timer = setInterval(refresh, interval)
  } catch (e) {
    error.value = e.message
    loading.value = false
  }
})

onBeforeUnmount(() => {
  if (timer) clearInterval(timer)
})
</script>

<style scoped>
.state.loading { color: #555; }
.state.error { color: #c00; }
.lane { border: 1px solid #ccc; padding: 8px; margin: 4px; display: inline-block; min-width: 120px; }
.marker { background: #eef; margin: 2px; padding: 2px; }
.pause-flag { color: #c60; }
.busy { margin-left: 8px; }
.error { color: #c00; }
.result { color: #060; }
</style>
