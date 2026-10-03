import { computeLayout, type LayoutInput } from './layout'

// Runs the force layout off the main thread; hundreds of nodes take noticeable time.
self.onmessage = (event: MessageEvent<{ input: LayoutInput; progressEvery: number }>) => {
  computeLayout(event.data.input, (positions, done) => self.postMessage({ positions, done }), event.data.progressEvery)
}
