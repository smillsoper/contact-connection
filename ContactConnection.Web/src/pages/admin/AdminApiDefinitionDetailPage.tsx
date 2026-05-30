import { useParams } from 'react-router'
import AdminShell from '../../components/admin/AdminShell'
import ApiDefinitionDetailContent, { type DetailApi } from '../../components/apiDefinitions/ApiDefinitionDetailContent'
import {
  getAdminApiDefinition,
  updateAdminApiDefinition,
  activateAdminApiDefinition,
  deactivateAdminApiDefinition,
  listAdminApiEndpoints,
  createAdminApiEndpoint,
  updateAdminApiEndpoint,
  deleteAdminApiEndpoint,
  testAdminAuth,
  testAdminEndpoint,
} from '../../api/adminApiDefinitions'
import { listAdminCredentials, setAdminCredential } from '../../api/adminCredentials'

const adminApi: DetailApi = {
  getDefinition: getAdminApiDefinition,
  updateDefinition: updateAdminApiDefinition,
  activateDefinition: activateAdminApiDefinition,
  deactivateDefinition: deactivateAdminApiDefinition,
  listEndpoints: listAdminApiEndpoints,
  createEndpoint: createAdminApiEndpoint,
  updateEndpoint: updateAdminApiEndpoint,
  deleteEndpoint: deleteAdminApiEndpoint,
  listCredentials: () => listAdminCredentials().then((list) => list.map((c) => c.keyName)),
  setCredential: setAdminCredential,
  testAuth: testAdminAuth,
  testEndpoint: testAdminEndpoint,
  listPagePath: '/admin/api-definitions',
}

export default function AdminApiDefinitionDetailPage() {
  const { id } = useParams<{ id: string }>()

  if (!id) return null

  return (
    <AdminShell>
      <ApiDefinitionDetailContent definitionId={id} api={adminApi} />
    </AdminShell>
  )
}
