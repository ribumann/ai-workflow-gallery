import { Routes, Route, Navigate, useParams } from 'react-router-dom'
import type { ReactNode } from 'react'
import { FluentProvider } from '@fluentui/react-components'
import { galleryTheme } from './branding'
import { AuthProvider } from './auth/AuthProvider'
import { RequireAuth } from './auth/RequireAuth'
import { AppShell } from './components/AppShell'
import { SearchPage } from './pages/Search/SearchPage'
import { TaskDetailPage } from './pages/TaskDetail/TaskDetailPage'
import { TaskEditorPage } from './pages/TaskEditor/TaskEditorPage'
import { WorkflowDetailPage } from './pages/WorkflowDetail/WorkflowDetailPage'
import { WorkflowEditorPage } from './pages/WorkflowEditor/WorkflowEditorPage'
import { ReportsPage } from './pages/Reports/ReportsPage'
import { ReportEditorPage } from './pages/Reports/ReportEditorPage'
import { ReportViewerPage } from './pages/Reports/ReportViewerPage'
import { MethodEditorPage } from './pages/Methods/MethodEditorPage'
import { MethodDetailPage } from './pages/Methods/MethodDetailPage'
import { IntegrationGuidePage } from './pages/Integrations/IntegrationGuidePage'
import { CollectionBar } from './components/CollectionBar'
import { useAuth } from './auth/useAuth'
import { canWriteCollection } from './services/deployment'

/** Redirects an old `/artifacts/:id(/edit)` bookmark to its `/tasks/:id(/edit)` equivalent,
 * preserving the id (issue #76 renamed the "Artifact" concept to "Task"). */
function LegacyTaskRedirect({ edit = false }: { edit?: boolean }) {
  const { id } = useParams<{ id: string }>()
  return <Navigate to={`/tasks/${id}${edit ? '/edit' : ''}`} replace />
}

function WriteRoute({ fallback, children }: { fallback: string; children: ReactNode }) {
  const auth = useAuth()
  return !canWriteCollection(auth.publicDemo, auth.collectionCapabilities)
    ? <Navigate to={fallback} replace />
    : <>{children}</>
}

/** Root component (T027): wires React Router routes for Search, TaskDetail,
 * WorkflowDetail, and WorkflowEditor (create/edit, US3) behind the T022 auth guard,
 * inside the Fluent v9 theme provider. */
function App() {
  return (
    <FluentProvider theme={galleryTheme}>
      <AuthProvider>
        <RequireAuth>
          <AppShell>
            <CollectionBar />
            <Routes>
              <Route path="/" element={<SearchPage />} />
              {/* Methods are browsed from the unified discovery page; this keeps older
                  /methods bookmarks and links working. */}
              <Route path="/methods" element={<Navigate to="/?tab=methods" replace />} />
              <Route path="/methods/new" element={<WriteRoute fallback="/?tab=methods"><MethodEditorPage /></WriteRoute>} />
              <Route path="/methods/:id/edit" element={<WriteRoute fallback="/?tab=methods"><MethodEditorPage /></WriteRoute>} />
              <Route path="/methods/:id" element={<MethodDetailPage />} />
              <Route path="/reports" element={<ReportsPage />} />
              <Route path="/reports/new" element={<WriteRoute fallback="/reports"><ReportEditorPage /></WriteRoute>} />
              <Route path="/reports/:id/edit" element={<WriteRoute fallback="/reports"><ReportEditorPage /></WriteRoute>} />
              <Route path="/reports/:id" element={<ReportViewerPage />} />
              <Route
                path="/integrations"
                element={<Navigate to="/integrations/github-copilot" replace />}
              />
              <Route path="/integrations/:id" element={<IntegrationGuidePage />} />
              <Route path="/tasks/new" element={<WriteRoute fallback="/"><TaskEditorPage /></WriteRoute>} />
              <Route path="/tasks/:id" element={<TaskDetailPage />} />
              <Route path="/workflows/new" element={<WriteRoute fallback="/?tab=workflows"><WorkflowEditorPage /></WriteRoute>} />
              <Route path="/tasks/:id/edit" element={<WriteRoute fallback="/"><TaskEditorPage /></WriteRoute>} />
              <Route path="/workflows/:id" element={<WorkflowDetailPage />} />
              <Route path="/workflows/:id/edit" element={<WriteRoute fallback="/?tab=workflows"><WorkflowEditorPage /></WriteRoute>} />
              {/* Legacy redirects: routes were named /artifacts/* before issue #76 renamed
                  the "Artifact" concept to "Task". Kept so any old bookmarks still work. */}
              <Route path="/artifacts/new" element={<Navigate to="/tasks/new" replace />} />
              <Route path="/artifacts/:id/edit" element={<LegacyTaskRedirect edit />} />
              <Route path="/artifacts/:id" element={<LegacyTaskRedirect />} />
              <Route path="*" element={<Navigate to="/" replace />} />
            </Routes>
          </AppShell>
        </RequireAuth>
      </AuthProvider>
    </FluentProvider>
  )
}

export default App
